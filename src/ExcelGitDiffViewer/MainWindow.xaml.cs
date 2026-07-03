using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
    // キーボードショートカット用の RoutedUICommand（A-1）。
    // XAML の <Window.InputBindings> / <Window.CommandBindings> から x:Static で参照する。
    public static readonly RoutedUICommand NextDiffCommand = new("次の差分へ", nameof(NextDiffCommand), typeof(MainWindow));
    public static readonly RoutedUICommand PrevDiffCommand = new("前の差分へ", nameof(PrevDiffCommand), typeof(MainWindow));
    public static readonly RoutedUICommand HomeViewCommand = new("ホーム画面", nameof(HomeViewCommand), typeof(MainWindow));
    public static readonly RoutedUICommand DataViewCommand = new("データ・数式ビュー", nameof(DataViewCommand), typeof(MainWindow));
    public static readonly RoutedUICommand VbaViewCommand = new("VBA コードビュー", nameof(VbaViewCommand), typeof(MainWindow));
    public static readonly RoutedUICommand ToggleReviewCommand = new("レビューモード切替", nameof(ToggleReviewCommand), typeof(MainWindow));
    public static readonly RoutedUICommand PrevSheetCommand = new("前のシート / モジュール", nameof(PrevSheetCommand), typeof(MainWindow));
    public static readonly RoutedUICommand NextSheetCommand = new("次のシート / モジュール", nameof(NextSheetCommand), typeof(MainWindow));

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
            // 全シート検索でシートが切り替わった直後は列生成タイミングに合わせて再度スクロールする必要がある。
            if (_viewModel?.CurrentMatch is not null)
            {
                Dispatcher.BeginInvoke(new System.Action(JumpToCurrentSearchMatch),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentView) && _viewModel?.IsDataView != true)
        {
            HideDetail();
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedVbaModule))
        {
            _vbaCurrentIndex = -1;
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentMatch))
        {
            JumpToCurrentSearchMatch();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsSearchBarVisible)
                 && _viewModel?.IsSearchBarVisible == true)
        {
            // 検索バーが開いた直後は TextBox にフォーカスを移して全選択（Ctrl+F 直後の再検索がしやすい）。
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                SearchTextBox.Focus();
                SearchTextBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void OnShowDataView(object sender, RoutedEventArgs e) => _viewModel?.ShowDataView();

    private void OnShowVbaView(object sender, RoutedEventArgs e) => _viewModel?.ShowVbaView();

    private void OnShowHomeClick(object sender, RoutedEventArgs e) => _viewModel?.ShowHomeView();

    // ホーム画面の2カードは、既存のファイル選択 / Git 選択フローへ委譲する。
    private void OnHomeCompareFilesClick(object sender, RoutedEventArgs e) => OnOpenFilesClick(sender, e);

    private void OnHomeCompareGitClick(object sender, RoutedEventArgs e) => OnOpenFromGitClick(sender, e);

    /// <summary>ホーム画面「最近開いた比較」のエントリクリック。Files はそのまま、Git は SHA から一時ファイルを再展開して比較開始。</summary>
    private async void OnRecentEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not RecentEntry entry || _viewModel == null)
        {
            return;
        }

        if (entry.Kind == RecentKind.Files)
        {
            if (string.IsNullOrEmpty(entry.LeftPath) || string.IsNullOrEmpty(entry.RightPath) ||
                !System.IO.File.Exists(entry.LeftPath) || !System.IO.File.Exists(entry.RightPath))
            {
                MessageBox.Show(this,
                    "このエントリのファイルが見つかりませんでした。履歴から削除します。",
                    "ファイルが見つかりません", MessageBoxButton.OK, MessageBoxImage.Warning);
                _viewModel.RemoveRecentEntry(entry);
                return;
            }

            var refreshed = new RecentEntry
            {
                Kind = RecentKind.Files,
                LeftPath = entry.LeftPath,
                RightPath = entry.RightPath,
                LeftLabel = entry.LeftLabel,
                RightLabel = entry.RightLabel,
                TimestampUtc = System.DateTime.UtcNow,
            };
            await _viewModel.LoadAsync(entry.LeftPath, entry.RightPath, recentEntry: refreshed);
            return;
        }

        // Git 比較の履歴：SHA から一時ファイルを再展開する。
        if (string.IsNullOrEmpty(entry.RepoRoot) || string.IsNullOrEmpty(entry.RelativePath) ||
            !System.IO.Directory.Exists(entry.RepoRoot))
        {
            MessageBox.Show(this,
                "このエントリのリポジトリが見つかりませんでした。履歴から削除します。",
                "リポジトリが見つかりません", MessageBoxButton.OK, MessageBoxImage.Warning);
            _viewModel.RemoveRecentEntry(entry);
            return;
        }

        var leftInfo = entry.LeftSha == null ? GitCommitInfo.WorkingTree : new GitCommitInfo(entry.LeftSha, entry.LeftLabel);
        var rightInfo = entry.RightSha == null ? GitCommitInfo.WorkingTree : new GitCommitInfo(entry.RightSha, entry.RightLabel);

        string? leftPath = GitService.RestoreToTemp(entry.RepoRoot, entry.RelativePath, leftInfo);
        string? rightPath = GitService.RestoreToTemp(entry.RepoRoot, entry.RelativePath, rightInfo);
        if (leftPath == null || rightPath == null)
        {
            MessageBox.Show(this,
                "指定したリビジョンからファイルを復元できませんでした。履歴から削除します。",
                "復元失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
            _viewModel.RemoveRecentEntry(entry);
            return;
        }

        var refreshedGit = new RecentEntry
        {
            Kind = RecentKind.Git,
            RepoRoot = entry.RepoRoot,
            RelativePath = entry.RelativePath,
            LeftSha = entry.LeftSha,
            RightSha = entry.RightSha,
            LeftLabel = entry.LeftLabel,
            RightLabel = entry.RightLabel,
            TimestampUtc = System.DateTime.UtcNow,
        };
        await _viewModel.LoadAsync(leftPath, rightPath,
            leftLabel: entry.LeftLabel,
            rightLabel: entry.RightLabel,
            recentEntry: refreshedGit);
    }

    private void OnNextDiffClick(object sender, RoutedEventArgs e) => MoveToDiff(forward: true);

    private void OnPrevDiffClick(object sender, RoutedEventArgs e) => MoveToDiff(forward: false);

    // ── キーボードショートカット (A-1) の Executed ハンドラ群 ──
    // 既存のクリックハンドラとロジックを共有するため、CommandBinding.Executed から共通処理へ委譲する。

    private void OnNextDiffCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (_viewModel?.IsLoaded == true)
        {
            MoveToDiff(forward: true);
        }
    }

    private void OnPrevDiffCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (_viewModel?.IsLoaded == true)
        {
            MoveToDiff(forward: false);
        }
    }

    private void OnHomeViewCommand(object sender, ExecutedRoutedEventArgs e) => _viewModel?.ShowHomeView();

    private void OnDataViewCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (_viewModel?.IsLoaded == true)
        {
            _viewModel.ShowDataView();
        }
    }

    private void OnVbaViewCommand(object sender, ExecutedRoutedEventArgs e) => _viewModel?.ShowVbaView();

    private void OnToggleReviewCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (_viewModel?.IsLoaded == true)
        {
            _viewModel.ToggleReviewMode();
        }
    }

    private void OnPrevSheetCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (_viewModel == null)
        {
            return;
        }

        if (_viewModel.IsDataView)
        {
            _viewModel.SelectPrevSheet();
        }
        else if (_viewModel.IsVbaView)
        {
            _viewModel.SelectPrevVbaModule();
        }
    }

    private void OnNextSheetCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (_viewModel == null)
        {
            return;
        }

        if (_viewModel.IsDataView)
        {
            _viewModel.SelectNextSheet();
        }
        else if (_viewModel.IsVbaView)
        {
            _viewModel.SelectNextVbaModule();
        }
    }

    /// <summary>Ctrl+O (ApplicationCommands.Open) → 既存のファイルオープンフロー。</summary>
    private void OnOpenCommand(object sender, ExecutedRoutedEventArgs e) => OnOpenFilesClick(sender, e);

    /// <summary>Ctrl+F (ApplicationCommands.Find) → 検索バーをトグル。読み込み前 / VBA ビュー時は無視。</summary>
    private void OnFindCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (_viewModel?.IsLoaded == true && _viewModel.IsDataView)
        {
            _viewModel.ToggleSearchBar();
        }
    }

    // ── 検索バー (A-2) の UI ハンドラ ──

    private void OnPrevMatchClick(object sender, RoutedEventArgs e) => _viewModel?.MoveMatch(forward: false);

    private void OnNextMatchClick(object sender, RoutedEventArgs e) => _viewModel?.MoveMatch(forward: true);

    private void OnCloseSearchClick(object sender, RoutedEventArgs e) => _viewModel?.CloseSearchBar();

    private void OnSearchTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel == null)
        {
            return;
        }

        // TextBox 内で Ctrl+F を吸い込まないよう、以下の3キーだけを検索操作にバインドする。
        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.MoveMatch(forward: (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) == 0);
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.CloseSearchBar();
                e.Handled = true;
                break;
        }
    }

    /// <summary>現在の <see cref="MainViewModel.CurrentMatch"/> の位置へスクロール＋セル選択でジャンプする。</summary>
    private void JumpToCurrentSearchMatch()
    {
        var match = _viewModel?.CurrentMatch;
        if (match == null)
        {
            return;
        }

        // シート切替が必要なら先に SelectedSheet を差し替える（列再生成完了後に SelectedSheet 変更ハンドラが再度この関数を呼び直す）。
        if (_viewModel!.SelectedSheet != match.Sheet)
        {
            _viewModel.SelectedSheet = match.Sheet;
            return;
        }

        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            var grid = match.IsLeft ? LeftGrid : RightGrid;
            var rows = match.IsLeft ? match.Sheet.DisplayLeftRows : match.Sheet.DisplayRightRows;
            if (match.RowIndex < 0 || match.RowIndex >= rows.Count)
            {
                return;
            }

            // 列 0 は行番号列。データ列は 1-based。
            int gridColumnIndex = match.ColumnIndex + 1;
            if (gridColumnIndex >= grid.Columns.Count)
            {
                return;
            }

            var row = rows[match.RowIndex];
            grid.ScrollIntoView(row);
            var cell = new DataGridCellInfo(row, grid.Columns[gridColumnIndex]);
            grid.CurrentCell = cell;
            grid.SelectedCells.Clear();
            grid.SelectedCells.Add(cell);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

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
            var recent = new RecentEntry
            {
                Kind = RecentKind.Files,
                LeftPath = leftDialog.FileName,
                RightPath = rightDialog.FileName,
                LeftLabel = System.IO.Path.GetFileName(leftDialog.FileName),
                RightLabel = System.IO.Path.GetFileName(rightDialog.FileName),
                TimestampUtc = System.DateTime.UtcNow,
            };
            await _viewModel.LoadAsync(leftDialog.FileName, rightDialog.FileName, recentEntry: recent);
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
            string leftLabel = $"{name} @ {ShortLabel(picker.SelectedLeft)}";
            string rightLabel = $"{name} @ {ShortLabel(picker.SelectedRight)}";
            var recent = new RecentEntry
            {
                Kind = RecentKind.Git,
                RepoRoot = repoRoot,
                RelativePath = relativePath,
                LeftSha = picker.SelectedLeft.Sha,
                RightSha = picker.SelectedRight.Sha,
                LeftLabel = leftLabel,
                RightLabel = rightLabel,
                TimestampUtc = System.DateTime.UtcNow,
            };
            await _viewModel.LoadAsync(leftPath, rightPath,
                leftLabel: leftLabel,
                rightLabel: rightLabel,
                recentEntry: recent);
        }
    }

    private static string ShortLabel(Services.GitCommitInfo info)
        => info.IsWorkingTree ? "ワークツリー" : info.Sha![..8];
}
