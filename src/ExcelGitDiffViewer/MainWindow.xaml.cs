using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ExcelGitDiffViewer.Converters;
using ExcelGitDiffViewer.Models;
using ExcelGitDiffViewer.Services;
using ExcelGitDiffViewer.Theme;
using ExcelGitDiffViewer.ViewModels;
using Microsoft.Win32;

namespace ExcelGitDiffViewer;

/// <summary>
/// メインウィンドウ。選択シートに応じた DataGrid の列動的生成と左右スクロール同期を担う。
/// </summary>
public partial class MainWindow : Window
{
    private readonly DiffKindToBrushConverter _brushConverter = new();
    private readonly BooleanToVisibilityConverter _boolToVis = new();

    private MainViewModel? _viewModel;
    private ScrollViewer? _leftScroll;
    private ScrollViewer? _rightScroll;
    private bool _syncing;
    private int _vbaCurrentIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as MainViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            RebuildColumns();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedSheet))
        {
            RebuildColumns();
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentView) && _viewModel?.IsDataView != true)
        {
            HideDetail();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedVbaModule))
        {
            _vbaCurrentIndex = -1;
        }
    }

    private void OnShowDataView(object sender, RoutedEventArgs e) => _viewModel?.ShowDataView();

    private void OnShowVbaView(object sender, RoutedEventArgs e) => _viewModel?.ShowVbaView();

    private void OnShowHomeClick(object sender, RoutedEventArgs e) => _viewModel?.ShowHomeView();

    // ホーム画面の2カードは、既存のファイル選択 / Git 選択フローへ委譲する。
    private void OnHomeCompareFilesClick(object sender, RoutedEventArgs e) => OnOpenFilesClick(sender, e);

    private void OnHomeCompareGitClick(object sender, RoutedEventArgs e) => OnOpenFromGitClick(sender, e);

    private void OnNextDiffClick(object sender, RoutedEventArgs e) => MoveToDiff(forward: true);

    private void OnPrevDiffClick(object sender, RoutedEventArgs e) => MoveToDiff(forward: false);

    /// <summary>現在の選択行から次（または前）の差分行へスクロールし、その行の先頭データ列を選択する。</summary>
    private void MoveToDiff(bool forward)
    {
        if (_viewModel?.IsVbaView == true)
        {
            MoveToVbaDiff(forward);
            return;
        }

        var sheet = _viewModel?.SelectedSheet;
        if (sheet == null)
        {
            return;
        }

        var leftRows = sheet.DisplayLeftRows;
        var rightRows = sheet.DisplayRightRows;
        int count = leftRows.Count;
        if (count == 0)
        {
            return;
        }

        int start = LeftGrid.Items.IndexOf(LeftGrid.CurrentItem);
        if (start < 0)
        {
            start = forward ? -1 : count;
        }

        int step = forward ? 1 : -1;
        for (int i = start + step; i >= 0 && i < count; i += step)
        {
            if (!SheetTabViewModel.RowHasDiff(leftRows[i]) && !SheetTabViewModel.RowHasDiff(rightRows[i]))
            {
                continue;
            }

            var leftRow = leftRows[i];
            var rightRow = rightRows[i];
            LeftGrid.ScrollIntoView(leftRow);
            RightGrid.ScrollIntoView(rightRow);

            // 仮想化下では行コンテナ生成後に選択する必要があるため、後追いで選択する。
            Dispatcher.BeginInvoke(
                new System.Action(() =>
                {
                    if (LeftGrid.Columns.Count > 1)
                    {
                        var cell = new DataGridCellInfo(leftRow, LeftGrid.Columns[1]);
                        LeftGrid.CurrentCell = cell;
                        LeftGrid.SelectedCells.Clear();
                        LeftGrid.SelectedCells.Add(cell);
                    }
                }),
                System.Windows.Threading.DispatcherPriority.Background);
            break;
        }
    }

    /// <summary>
    /// VBA コードビュー時、選択モジュールの差分行間を上下移動する。
    /// レビュー ON では DisplayLines が既に差分行のみなので隣接インデックスを採用、OFF では Unchanged 行をスキップする。
    /// </summary>
    private void MoveToVbaDiff(bool forward)
    {
        var module = _viewModel?.SelectedVbaModule;
        if (module == null)
        {
            return;
        }

        var lines = module.DisplayLines;
        int count = lines.Count;
        if (count == 0)
        {
            return;
        }

        // モード切替やモジュール切替で範囲外になった現在位置をクランプ。
        if (_vbaCurrentIndex >= count)
        {
            _vbaCurrentIndex = count;
        }
        else if (_vbaCurrentIndex < -1)
        {
            _vbaCurrentIndex = -1;
        }

        int step = forward ? 1 : -1;
        int start = _vbaCurrentIndex;
        if (start < 0)
        {
            start = forward ? -1 : count;
        }

        for (int i = start + step; i >= 0 && i < count; i += step)
        {
            var line = lines[i];
            if (!module.IsReviewMode &&
                line.LeftKind == DiffKind.Unchanged && line.RightKind == DiffKind.Unchanged)
            {
                continue;
            }

            _vbaCurrentIndex = i;
            var target = i;
            Dispatcher.BeginInvoke(
                new System.Action(() =>
                {
                    if (VbaLinesItems.ItemContainerGenerator.ContainerFromIndex(target)
                        is FrameworkElement container)
                    {
                        container.BringIntoView();
                    }
                }),
                System.Windows.Threading.DispatcherPriority.Background);
            break;
        }
    }

    /// <summary>選択シートの列数に合わせて左右 DataGrid の列を再生成する。</summary>
    private void RebuildColumns()
    {
        HideDetail();
        var sheet = _viewModel?.SelectedSheet;
        if (sheet == null)
        {
            LeftGrid.Columns.Clear();
            RightGrid.Columns.Clear();
            return;
        }

        BuildColumns(LeftGrid, sheet.ColumnCount);
        BuildColumns(RightGrid, sheet.ColumnCount);
    }

    /// <summary>セル選択時に、その座標の値・数式差分を下部詳細パネルへ表示する。</summary>
    private void OnSelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (sender is not DataGrid grid || grid.SelectedCells.Count == 0)
        {
            return;
        }

        var info = grid.CurrentCell;
        if (!info.IsValid || info.Column == null || info.Item is not RowModel row)
        {
            return;
        }

        int colIndex = grid.Columns.IndexOf(info.Column) - 1; // 列0 は行番号列。
        var sheet = _viewModel?.SelectedSheet;
        if (colIndex < 0 || sheet == null)
        {
            HideDetail();
            return;
        }

        int idx = row.AlignedIndex;
        if (idx < 0 || idx >= sheet.LeftRows.Count || idx >= sheet.RightRows.Count)
        {
            return;
        }

        var leftCell = sheet.LeftRows[idx].Cells[colIndex];
        var rightCell = sheet.RightRows[idx].Cells[colIndex];
        ShowDetail(colIndex, sheet.LeftRows[idx], sheet.RightRows[idx], leftCell, rightCell);
    }

    private void ShowDetail(int colIndex, RowModel leftRow, RowModel rightRow, CellModel leftCell, CellModel rightCell)
    {
        bool hasFormula = leftCell.HasFormula || rightCell.HasFormula;
        string leftText = leftCell.Formula ?? leftCell.Display;
        string rightText = rightCell.Formula ?? rightCell.Display;

        if (string.IsNullOrEmpty(leftText) && string.IsNullOrEmpty(rightText))
        {
            HideDetail();
            return;
        }

        int? rowNum = leftRow.RowNumber ?? rightRow.RowNumber;
        DetailHeader.Text = $"選択セル ({ToColumnName(colIndex)}{rowNum}) の{(hasFormula ? "数式" : "値")}差分";

        var (oldSegs, newSegs) = InlineTextDiff.Diff(leftText, rightText);
        PopulateInlines(DetailBeforeLine, oldSegs);
        PopulateInlines(DetailAfterLine, newSegs);
        DetailPanel.Visibility = Visibility.Visible;
    }

    private void HideDetail() => DetailPanel.Visibility = Visibility.Collapsed;

    /// <summary>インライン差分セグメントを TextBlock の Inlines として描画する（配色はテーマから取得）。</summary>
    private static void PopulateInlines(TextBlock target, IReadOnlyList<InlineSegment> segments)
    {
        target.Inlines.Clear();
        if (segments.Count == 0)
        {
            target.Inlines.Add(new System.Windows.Documents.Run("(空)")
            {
                Foreground = ThemeResources.Brush(ThemeKeys.FgMuted, Brushes.Gray),
            });
            return;
        }

        var deletedBg = ThemeResources.Brush(ThemeKeys.InlineDelBg);
        var deletedFg = ThemeResources.Brush(ThemeKeys.InlineDelFg);
        var insertedBg = ThemeResources.Brush(ThemeKeys.InlineInsBg);
        var insertedFg = ThemeResources.Brush(ThemeKeys.InlineInsFg);

        foreach (var seg in segments)
        {
            var run = new System.Windows.Documents.Run(seg.Text);
            switch (seg.Kind)
            {
                case InlineSegmentKind.Deleted:
                    run.Background = deletedBg;
                    run.Foreground = deletedFg;
                    break;
                case InlineSegmentKind.Inserted:
                    run.Background = insertedBg;
                    run.Foreground = insertedFg;
                    break;
            }

            target.Inlines.Add(run);
        }
    }

    /// <summary>行番号列＋値列（差分背景色付き）を生成する。</summary>
    private void BuildColumns(DataGrid grid, int columnCount)
    {
        grid.Columns.Clear();

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Binding = new Binding(nameof(RowModel.RowNumber)),
            IsReadOnly = true,
            CellStyle = Application.Current?.TryFindResource(ThemeKeys.RowNumberCellStyle) as Style,
            ElementStyle = Application.Current?.TryFindResource(ThemeKeys.RowNumberTextStyle) as Style,
        });

        for (int i = 0; i < columnCount; i++)
        {
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = ToColumnName(i),
                CellTemplate = BuildCellTemplate(i),
                MinWidth = 48,
            });
        }
    }

    /// <summary>
    /// セル1個分のテンプレート（背景=差分色の Border ＋ 値の TextBlock ＋ 数式インジケータ）を生成する。
    /// </summary>
    private DataTemplate BuildCellTemplate(int columnIndex)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(
            Border.BackgroundProperty,
            new Binding($"Cells[{columnIndex}].Diff") { Converter = _brushConverter });
        border.SetValue(Border.PaddingProperty, new Thickness(5, 2, 5, 2));

        var dock = new FrameworkElementFactory(typeof(DockPanel));
        dock.SetValue(DockPanel.LastChildFillProperty, true);

        // 数式インジケータ（右寄せの "ƒ"）。数式セルのみ表示。
        var marker = new FrameworkElementFactory(typeof(TextBlock));
        marker.SetValue(TextBlock.TextProperty, "ƒ");
        marker.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        marker.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.MarkerFormulaFg);
        marker.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 0, 0));
        marker.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        marker.SetValue(DockPanel.DockProperty, Dock.Right);
        marker.SetBinding(
            TextBlock.VisibilityProperty,
            new Binding($"Cells[{columnIndex}].HasFormula") { Converter = _boolToVis });
        dock.AppendChild(marker);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding($"Cells[{columnIndex}].Display"));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        // DataGridTemplateColumn のセルテンプレート内では DataGridCell.Foreground が継承されず
        // 既定の黒文字になりやすい。テーマの前景色を明示指定して視認性を確保する。
        text.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.FgPrimary);
        dock.AppendChild(text);

        border.AppendChild(dock);
        return new DataTemplate { VisualTree = border };
    }

    /// <summary>0始まり列インデックスを Excel 風の列名（A, B, …, Z, AA, …）に変換する。</summary>
    private static string ToColumnName(int index)
    {
        var sb = new StringBuilder();
        int n = index + 1;
        while (n > 0)
        {
            int rem = (n - 1) % 26;
            sb.Insert(0, (char)('A' + rem));
            n = (n - 1) / 26;
        }

        return sb.ToString();
    }

    /// <summary>左右 DataGrid のスクロールを相互同期する。</summary>
    private void OnGridScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_syncing || e.OriginalSource is not ScrollViewer source)
        {
            return;
        }

        // 内部 ScrollViewer をキャッシュ。
        if (ReferenceEquals(sender, LeftGrid))
        {
            _leftScroll = source;
        }
        else if (ReferenceEquals(sender, RightGrid))
        {
            _rightScroll = source;
        }

        var target = ReferenceEquals(sender, LeftGrid) ? _rightScroll : _leftScroll;
        if (target == null)
        {
            return;
        }

        _syncing = true;
        try
        {
            if (e.VerticalChange != 0)
            {
                target.ScrollToVerticalOffset(source.VerticalOffset);
            }

            if (e.HorizontalChange != 0)
            {
                target.ScrollToHorizontalOffset(source.HorizontalOffset);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private async void OnOpenFilesClick(object sender, RoutedEventArgs e)
    {
        const string filter = "Excel ファイル (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|すべてのファイル (*.*)|*.*";

        var leftDialog = new OpenFileDialog { Title = "変更前（左）のファイルを選択", Filter = filter };
        if (leftDialog.ShowDialog(this) != true)
        {
            return;
        }

        var rightDialog = new OpenFileDialog { Title = "変更後（右）のファイルを選択", Filter = filter };
        if (rightDialog.ShowDialog(this) != true)
        {
            return;
        }

        if (_viewModel != null)
        {
            await _viewModel.LoadAsync(leftDialog.FileName, rightDialog.FileName);
        }
    }

    /// <summary>
    /// Git リポジトリ内の Excel を選び、2つのコミット間（またはワークツリー）で比較する（仕様 §3.3）。
    /// </summary>
    private async void OnOpenFromGitClick(object sender, RoutedEventArgs e)
    {
        const string filter = "Excel ファイル (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|すべてのファイル (*.*)|*.*";
        var dialog = new OpenFileDialog { Title = "Git 管理下の Excel ファイルを選択", Filter = filter };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!GitService.TryLocateRepository(dialog.FileName, out var repoRoot, out var relativePath))
        {
            MessageBox.Show(this,
                "選択したファイルは Git リポジトリの管理下にありません。",
                "Git リポジトリが見つかりません", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var history = GitService.GetHistory(repoRoot, relativePath);
        if (history.Count <= 1)
        {
            MessageBox.Show(this,
                "このファイルのコミット履歴が見つかりませんでした。",
                "履歴なし", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new CommitPickerWindow(relativePath, history) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedLeft == null || picker.SelectedRight == null)
        {
            return;
        }

        string? leftPath = GitService.RestoreToTemp(repoRoot, relativePath, picker.SelectedLeft);
        string? rightPath = GitService.RestoreToTemp(repoRoot, relativePath, picker.SelectedRight);
        if (leftPath == null || rightPath == null)
        {
            MessageBox.Show(this,
                "指定したリビジョンからファイルを復元できませんでした。",
                "復元失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_viewModel != null)
        {
            string name = System.IO.Path.GetFileName(relativePath);
            await _viewModel.LoadAsync(leftPath, rightPath,
                leftLabel: $"{name} @ {ShortLabel(picker.SelectedLeft)}",
                rightLabel: $"{name} @ {ShortLabel(picker.SelectedRight)}");
        }
    }

    private static string ShortLabel(Services.GitCommitInfo info)
        => info.IsWorkingTree ? "ワークツリー" : info.Sha![..8];
}
