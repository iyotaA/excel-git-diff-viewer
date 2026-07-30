using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using ExcelGitDiffViewer.Models;
using ExcelGitDiffViewer.Services;
using ExcelGitDiffViewer.Theme;

namespace ExcelGitDiffViewer.ViewModels;

/// <summary>表示モード（ホーム / データ・数式ビュー / VBAコードビュー, 仕様 §4）。</summary>
public enum ViewMode
{
    Home,
    DataFormula,
    Vba,
}

/// <summary>差分内検索の1ヒット位置（A-2）。左右どちらの DataGrid の何行目・何列目にヒットしたかを保持する。</summary>
internal sealed record SearchMatch(SheetTabViewModel Sheet, int RowIndex, int ColumnIndex, bool IsLeft);

/// <summary>
/// メイン画面の ViewModel。2ファイルの読み込み・差分計算・シートタブ／VBA タブ生成を担う。
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _leftFilePath = string.Empty;
    private string _rightFilePath = string.Empty;
    private string? _errorMessage;
    private bool _isLoaded;
    private bool _isBusy;
    private SheetTabViewModel? _selectedSheet;
    private VbaModuleDiffViewModel? _selectedVbaModule;
    private bool _hasVba;
    private ViewMode _currentView = ViewMode.Home;
    private bool _isReviewMode;
    private bool _showAdded = true;
    private bool _showModified = true;
    private bool _showRemoved = true;
    private bool _filterRows = true;
    private bool _filterColumns = true;

    // 差分内検索 (A-2)
    private bool _isSearchBarVisible;
    private string _searchQuery = string.Empty;
    private bool _searchAllSheets;
    private int _currentMatchIndex = -1;
    private string _searchStatus = string.Empty;
    private readonly List<SearchMatch> _searchMatches = new();
    private readonly DispatcherTimer _searchDebounceTimer;

    /// <summary>左（変更前）の表示用パス。</summary>
    public string LeftFilePath
    {
        get => _leftFilePath;
        private set => SetField(ref _leftFilePath, value);
    }

    /// <summary>右（変更後）の表示用パス。</summary>
    public string RightFilePath
    {
        get => _rightFilePath;
        private set => SetField(ref _rightFilePath, value);
    }

    /// <summary>エラーメッセージ（null のとき正常）。</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ShowHome));
                OnPropertyChanged(nameof(HasSummary));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    /// <summary>2ファイルの読み込みと差分計算が完了したか。</summary>
    public bool IsLoaded
    {
        get => _isLoaded;
        private set
        {
            if (SetField(ref _isLoaded, value))
            {
                OnPropertyChanged(nameof(ShowDataArea));
                OnPropertyChanged(nameof(ShowVbaArea));
                OnPropertyChanged(nameof(ShowReviewToolbar));
                RaiseSummaryChanged();
            }
        }
    }

    /// <summary>読み込み・差分計算の実行中か（ローディング表示用）。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowHome));
                OnPropertyChanged(nameof(HasSummary));
            }
        }
    }

    /// <summary>シートタブ群（全件保持）。</summary>
    public ObservableCollection<SheetTabViewModel> Sheets { get; } = new();

    /// <summary>タブに表示するシート（レビュー ON 時は差分ありのみ）。</summary>
    public ObservableCollection<SheetTabViewModel> DisplaySheets { get; } = new();

    /// <summary>現在選択中のシート。</summary>
    public SheetTabViewModel? SelectedSheet
    {
        get => _selectedSheet;
        set
        {
            if (SetField(ref _selectedSheet, value))
            {
                // 現在シートのみ検索中はシート切替でヒット集合が変わる。全シート検索中は集合は変わらないので再計算不要。
                if (!_searchAllSheets && !string.IsNullOrEmpty(_searchQuery))
                {
                    RebuildSearchMatches();
                }
            }
        }
    }

    /// <summary>レビューモード（差分のみ表示）。シート／VBA モジュール双方に伝播し、VBA 一覧も絞り込む。</summary>
    public bool IsReviewMode
    {
        get => _isReviewMode;
        set
        {
            if (SetField(ref _isReviewMode, value))
            {
                foreach (var sheet in Sheets)
                {
                    sheet.IsReviewMode = value;
                }

                foreach (var module in VbaModules)
                {
                    module.IsReviewMode = value;
                }

                RebuildDisplaySheets();

                // フィルタで選択中のシートが消えた場合は先頭に再選択する。
                if (SelectedSheet == null || !DisplaySheets.Contains(SelectedSheet))
                {
                    SelectedSheet = DisplaySheets.Count > 0 ? DisplaySheets[0] : null;
                }

                RebuildDisplayVbaModules();

                // フィルタで選択中のモジュールが消えた場合は先頭に再選択する。
                if (SelectedVbaModule == null || !DisplayVbaModules.Contains(SelectedVbaModule))
                {
                    SelectedVbaModule = DisplayVbaModules.Count > 0 ? DisplayVbaModules[0] : null;
                }
            }
        }
    }

    /// <summary>差分のある行だけを表示するか（レビューモードの下位フィルタ、既定 ON）。全シートへ伝播する。</summary>
    public bool FilterRows
    {
        get => _filterRows;
        set
        {
            if (SetField(ref _filterRows, value))
            {
                foreach (var sheet in Sheets)
                {
                    sheet.FilterRows = value;
                }
            }
        }
    }

    /// <summary>差分のある列だけを表示するか（レビューモードの下位フィルタ、既定 ON）。全シートへ伝播し、列は code-behind が再生成する。</summary>
    public bool FilterColumns
    {
        get => _filterColumns;
        set
        {
            if (SetField(ref _filterColumns, value))
            {
                foreach (var sheet in Sheets)
                {
                    sheet.FilterColumns = value;
                }
            }
        }
    }

    /// <summary>「追加」の差分行を表示するか（A-5）。いずれか OFF にするとレビューモードが自動 ON になる。</summary>
    public bool ShowAdded
    {
        get => _showAdded;
        set
        {
            if (SetField(ref _showAdded, value))
            {
                OnDiffKindFilterChanged();
            }
        }
    }

    /// <summary>「変更」の差分行を表示するか（A-5）。いずれか OFF にするとレビューモードが自動 ON になる。</summary>
    public bool ShowModified
    {
        get => _showModified;
        set
        {
            if (SetField(ref _showModified, value))
            {
                OnDiffKindFilterChanged();
            }
        }
    }

    /// <summary>「削除」の差分行を表示するか（A-5）。いずれか OFF にするとレビューモードが自動 ON になる。</summary>
    public bool ShowRemoved
    {
        get => _showRemoved;
        set
        {
            if (SetField(ref _showRemoved, value))
            {
                OnDiffKindFilterChanged();
            }
        }
    }

    /// <summary>
    /// フィルタ変更時の共通処理。設定を全シート・全 VBA モジュールへ伝播し、
    /// レビュー OFF で「一部を隠す」設定になった場合はレビューモードを自動 ON にする。
    /// </summary>
    private void OnDiffKindFilterChanged()
    {
        ApplyDiffKindFilter();

        // 「一部の差分種別だけ表示したい」という操作意図は、レビューモード（差分のみ表示）の下でのみ意味を持つ。
        // レビュー OFF の状態から OFF に切り替えたときはレビューを ON にして即座に反映する。
        bool anyHidden = !_showAdded || !_showModified || !_showRemoved;
        if (anyHidden && !_isReviewMode && IsLoaded)
        {
            IsReviewMode = true;
        }
    }

    /// <summary>現在のフィルタ設定を全シート・全 VBA モジュールへ伝播させる。</summary>
    private void ApplyDiffKindFilter()
    {
        foreach (var sheet in Sheets)
        {
            sheet.SetFilter(_showAdded, _showModified, _showRemoved);
        }

        foreach (var module in VbaModules)
        {
            module.SetFilter(_showAdded, _showModified, _showRemoved);
        }
    }

    /// <summary>VBA モジュール差分一覧（全件保持）。</summary>
    public ObservableCollection<VbaModuleDiffViewModel> VbaModules { get; } = new();

    /// <summary>モジュール一覧に表示する VBA モジュール（レビュー ON 時は変更ありのみ）。</summary>
    public ObservableCollection<VbaModuleDiffViewModel> DisplayVbaModules { get; } = new();

    /// <summary>「最近開いた比較」履歴（ホーム画面表示用、最新順、上限は <see cref="RecentsService.Limit"/>）。</summary>
    public ObservableCollection<RecentEntry> RecentEntries { get; } = new(RecentsService.Load());

    /// <summary>履歴があるか（ホーム画面での「最近開いた比較」セクション表示切替に使う）。</summary>
    public bool HasRecentEntries => RecentEntries.Count > 0;

    public MainViewModel()
    {
        RecentEntries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentEntries));

        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            RebuildSearchMatches();
        };
    }

    // ── 差分内検索 (A-2) ──

    /// <summary>検索バーの表示可否。<see cref="ToggleSearchBar"/> / <see cref="CloseSearchBar"/> で切り替える。</summary>
    public bool IsSearchBarVisible
    {
        get => _isSearchBarVisible;
        private set => SetField(ref _isSearchBarVisible, value);
    }

    /// <summary>検索クエリ。setter で 300ms デバウンス後に再検索が走る。</summary>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetField(ref _searchQuery, value ?? string.Empty))
            {
                // 素早い連続入力ではタイマーがリスタートされ、最後の入力から 300ms 経過後にだけ検索する。
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            }
        }
    }

    /// <summary>全シートを対象に検索するか（OFF なら現在シートのみ）。</summary>
    public bool SearchAllSheets
    {
        get => _searchAllSheets;
        set
        {
            if (SetField(ref _searchAllSheets, value))
            {
                RebuildSearchMatches();
            }
        }
    }

    /// <summary>現在フォーカスしているヒットのインデックス（0-based）。-1 は無効。</summary>
    public int CurrentMatchIndex
    {
        get => _currentMatchIndex;
        private set
        {
            if (SetField(ref _currentMatchIndex, value))
            {
                UpdateSearchStatus();
                OnPropertyChanged(nameof(CurrentMatch));
            }
        }
    }

    /// <summary>件数表示用のステータス文字列（例: "3 / 12 件"、ヒット0で "該当なし"）。</summary>
    public string SearchStatus
    {
        get => _searchStatus;
        private set => SetField(ref _searchStatus, value);
    }

    /// <summary>現在のヒット位置。CurrentMatchIndex が範囲外なら null。</summary>
    internal SearchMatch? CurrentMatch =>
        _currentMatchIndex >= 0 && _currentMatchIndex < _searchMatches.Count
            ? _searchMatches[_currentMatchIndex]
            : null;

    /// <summary>検索バーを開閉する（開いた場合はデフォルトで現在シートのみ、履歴クエリは保持）。</summary>
    public void ToggleSearchBar()
    {
        IsSearchBarVisible = !IsSearchBarVisible;
        if (IsSearchBarVisible && !string.IsNullOrEmpty(_searchQuery))
        {
            RebuildSearchMatches();
        }
    }

    /// <summary>検索バーを閉じる（クエリは保持し、次回開いたときに再利用可能）。</summary>
    public void CloseSearchBar() => IsSearchBarVisible = false;

    /// <summary>前 / 次のヒットへ移動する（両端で折り返し）。</summary>
    public void MoveMatch(bool forward)
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }

        int next = _currentMatchIndex + (forward ? 1 : -1);
        if (next < 0)
        {
            next = _searchMatches.Count - 1;
        }
        else if (next >= _searchMatches.Count)
        {
            next = 0;
        }

        CurrentMatchIndex = next;
    }

    /// <summary>
    /// 現在のクエリ・スコープでヒットリストを再構築する。左右のセル値（Display）と数式（Formula）を大文字小文字無視で検索する。
    /// レビューモード ON かつフィルタが効いている場合は表示中の行のみが検索対象。
    /// </summary>
    private void RebuildSearchMatches()
    {
        _searchMatches.Clear();

        string query = _searchQuery;
        if (string.IsNullOrEmpty(query) || !IsLoaded)
        {
            CurrentMatchIndex = -1;
            return;
        }

        IEnumerable<SheetTabViewModel> scope = _searchAllSheets
            ? Sheets
            : (SelectedSheet != null ? new[] { SelectedSheet } : System.Array.Empty<SheetTabViewModel>());

        foreach (var sheet in scope)
        {
            var leftRows = sheet.DisplayLeftRows;
            var rightRows = sheet.DisplayRightRows;
            int rowCount = System.Math.Min(leftRows.Count, rightRows.Count);
            for (int r = 0; r < rowCount; r++)
            {
                CollectRowMatches(sheet, r, leftRows[r], query, isLeft: true);
                CollectRowMatches(sheet, r, rightRows[r], query, isLeft: false);
            }
        }

        CurrentMatchIndex = _searchMatches.Count > 0 ? 0 : -1;
    }

    private void CollectRowMatches(SheetTabViewModel sheet, int rowIndex, RowModel row, string query, bool isLeft)
    {
        for (int c = 0; c < row.Cells.Count; c++)
        {
            var cell = row.Cells[c];
            if (CellMatchesQuery(cell, query))
            {
                _searchMatches.Add(new SearchMatch(sheet, rowIndex, c, isLeft));
            }
        }
    }

    private static bool CellMatchesQuery(CellModel cell, string query)
    {
        if (!string.IsNullOrEmpty(cell.Display) &&
            cell.Display.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(cell.Formula) &&
            cell.Formula.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    private void UpdateSearchStatus()
    {
        if (string.IsNullOrEmpty(_searchQuery))
        {
            SearchStatus = string.Empty;
        }
        else if (_searchMatches.Count == 0)
        {
            SearchStatus = "該当なし";
        }
        else
        {
            SearchStatus = $"{_currentMatchIndex + 1} / {_searchMatches.Count} 件";
        }
    }

    /// <summary>現在選択中の VBA モジュール。</summary>
    public VbaModuleDiffViewModel? SelectedVbaModule
    {
        get => _selectedVbaModule;
        set => SetField(ref _selectedVbaModule, value);
    }

    /// <summary>いずれかの側に VBA が存在するか（VBA ビュー切替の可否）。</summary>
    public bool HasVba
    {
        get => _hasVba;
        private set => SetField(ref _hasVba, value);
    }

    /// <summary>現在の表示モード。</summary>
    public ViewMode CurrentView
    {
        get => _currentView;
        set
        {
            if (SetField(ref _currentView, value))
            {
                OnPropertyChanged(nameof(IsHomeView));
                OnPropertyChanged(nameof(IsDataView));
                OnPropertyChanged(nameof(IsVbaView));
                OnPropertyChanged(nameof(ShowHome));
                OnPropertyChanged(nameof(ShowDataArea));
                OnPropertyChanged(nameof(ShowVbaArea));
                OnPropertyChanged(nameof(ShowReviewToolbar));
            }
        }
    }

    public bool IsHomeView => _currentView == ViewMode.Home;

    public bool IsDataView => _currentView == ViewMode.DataFormula;

    public bool IsVbaView => _currentView == ViewMode.Vba;

    /// <summary>ホーム画面（比較方法の選択）を表示すべきか。未読み込みでも表示する。</summary>
    public bool ShowHome => IsHomeView && !IsBusy && !HasError;

    /// <summary>データ・数式ビューを表示すべきか。</summary>
    public bool ShowDataArea => IsLoaded && IsDataView;

    /// <summary>VBA コードビューを表示すべきか。</summary>
    public bool ShowVbaArea => IsLoaded && IsVbaView;

    /// <summary>レビューモード操作 UI（CheckBox / 件数 / ジャンプボタン）を表示すべきか。データ／VBA どちらでも表示する。</summary>
    public bool ShowReviewToolbar => IsLoaded && (IsDataView || IsVbaView);

    // ── B-2 サマリー / 統計ダッシュボード ──

    /// <summary>サマリーバー（比較全体像）の表示可否。読み込み完了かつ実行中・エラーでないとき表示。</summary>
    public bool HasSummary => IsLoaded && !IsBusy && !HasError;

    /// <summary>差分を含むシートの数。</summary>
    public int ChangedSheetCount => Sheets.Count(s => s.DiffRowCount > 0);

    /// <summary>全シート合算の追加行数。</summary>
    public int TotalAddedRows => Sheets.Sum(s => s.AddedRowCount);

    /// <summary>全シート合算の削除行数。</summary>
    public int TotalRemovedRows => Sheets.Sum(s => s.RemovedRowCount);

    /// <summary>全シート合算の変更セル数（値/数式が変わったセルの数）。</summary>
    public int TotalChangedCells => Sheets.Sum(s => s.ChangedCellCount);

    /// <summary>差分を含む VBA モジュールの数。</summary>
    public int ChangedVbaModuleCount => VbaModules.Count(m => m.HasDiff);

    /// <summary>テーマ切替ボタンに表示するアイコン（現在ダークなら「☀」＝押下でライトへ、ライトなら「🌙」＝押下でダークへ）。</summary>
    public string ThemeToggleIcon => ThemeManager.Current == AppTheme.Dark ? "☀" : "🌙";

    /// <summary>サマリー系プロパティの再計算を UI に通知する（読み込み完了時に呼ぶ）。</summary>
    private void RaiseSummaryChanged()
    {
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(ChangedSheetCount));
        OnPropertyChanged(nameof(TotalAddedRows));
        OnPropertyChanged(nameof(TotalRemovedRows));
        OnPropertyChanged(nameof(TotalChangedCells));
        OnPropertyChanged(nameof(ChangedVbaModuleCount));
    }

    /// <summary>テーマ切替時に <see cref="ThemeToggleIcon"/> の再取得を UI に通知する。</summary>
    public void NotifyThemeChanged() => OnPropertyChanged(nameof(ThemeToggleIcon));

    public void ShowHomeView() => CurrentView = ViewMode.Home;

    public void ShowDataView() => CurrentView = ViewMode.DataFormula;

    public void ShowVbaView()
    {
        if (HasVba)
        {
            CurrentView = ViewMode.Vba;
        }
    }

    /// <summary>レビューモードを反転する（キーボードショートカット用）。</summary>
    public void ToggleReviewMode() => IsReviewMode = !IsReviewMode;

    /// <summary>データ・数式ビューの前のシートへ切り替える（末端なら折り返さない）。</summary>
    public void SelectPrevSheet() => ShiftSelectedSheet(-1);

    /// <summary>データ・数式ビューの次のシートへ切り替える（末端なら折り返さない）。</summary>
    public void SelectNextSheet() => ShiftSelectedSheet(+1);

    private void ShiftSelectedSheet(int delta)
    {
        if (DisplaySheets.Count == 0 || SelectedSheet == null)
        {
            return;
        }

        int idx = DisplaySheets.IndexOf(SelectedSheet);
        int next = idx + delta;
        if (next < 0 || next >= DisplaySheets.Count)
        {
            return;
        }

        SelectedSheet = DisplaySheets[next];
    }

    /// <summary>VBA コードビューの前のモジュールへ切り替える。</summary>
    public void SelectPrevVbaModule() => ShiftSelectedVbaModule(-1);

    /// <summary>VBA コードビューの次のモジュールへ切り替える。</summary>
    public void SelectNextVbaModule() => ShiftSelectedVbaModule(+1);

    private void ShiftSelectedVbaModule(int delta)
    {
        if (DisplayVbaModules.Count == 0 || SelectedVbaModule == null)
        {
            return;
        }

        int idx = DisplayVbaModules.IndexOf(SelectedVbaModule);
        int next = idx + delta;
        if (next < 0 || next >= DisplayVbaModules.Count)
        {
            return;
        }

        SelectedVbaModule = DisplayVbaModules[next];
    }

    /// <summary>
    /// モジュール一覧の表示コレクションを再構築する。レビュー ON 時は変更ありのみ、OFF 時は全件。
    /// </summary>
    private void RebuildDisplayVbaModules()
    {
        DisplayVbaModules.Clear();
        foreach (var module in VbaModules)
        {
            if (!_isReviewMode || module.HasDiff)
            {
                DisplayVbaModules.Add(module);
            }
        }
    }

    /// <summary>
    /// シートタブの表示コレクションを再構築する。レビュー ON 時は差分ありのみ、OFF 時は全件。
    /// </summary>
    private void RebuildDisplaySheets()
    {
        DisplaySheets.Clear();
        foreach (var sheet in Sheets)
        {
            if (!_isReviewMode || sheet.HasDiff)
            {
                DisplaySheets.Add(sheet);
            }
        }
    }

    /// <summary>バックグラウンドで計算した差分結果（UI スレッドへ受け渡す中間データ）。</summary>
    private sealed record LoadResult(
        IReadOnlyList<SheetDiffModel> Sheets,
        IReadOnlyList<VbaModuleDiff> VbaModules);

    /// <summary>
    /// 左右2ファイルを非同期に読み込み差分を計算する。重い処理は別スレッドで実行し、
    /// UI フリーズを防ぐ。失敗時は <see cref="ErrorMessage"/> を設定する。
    /// </summary>
    /// <param name="leftLabel">情報バー表示用のラベル（省略時はパス）。git 比較時のコミット名等に使用。</param>
    /// <param name="recentEntry">読み込み成功時に <see cref="RecentEntries"/> へ追加するエントリ。null なら追加しない（履歴からの再オープンなど）。</param>
    public async Task LoadAsync(string leftPath, string rightPath, string? leftLabel = null, string? rightLabel = null, RecentEntry? recentEntry = null)
    {
        LeftFilePath = leftLabel ?? leftPath;
        RightFilePath = rightLabel ?? rightPath;
        ErrorMessage = null;
        IsLoaded = false;
        SelectedSheet = null;
        SelectedVbaModule = null;
        CurrentView = ViewMode.DataFormula;
        Sheets.Clear();
        VbaModules.Clear();
        DisplayVbaModules.Clear();
        HasVba = false;
        IsBusy = true;

        // 以前のファイルに紐付いた検索ヒットは無効になる。次に検索するまでバーは開いていても件数はゼロ。
        _searchMatches.Clear();
        CurrentMatchIndex = -1;

        try
        {
            var result = await Task.Run(() => Compute(leftPath, rightPath)).ConfigureAwait(true);

            foreach (var diff in result.Sheets)
            {
                var sheet = new SheetTabViewModel(diff)
                {
                    IsReviewMode = _isReviewMode,
                    FilterRows = _filterRows,
                    FilterColumns = _filterColumns,
                };
                sheet.SetFilter(_showAdded, _showModified, _showRemoved);
                Sheets.Add(sheet);
            }

            RebuildDisplaySheets();
            SelectedSheet = DisplaySheets.Count > 0 ? DisplaySheets[0] : null;

            foreach (var d in result.VbaModules)
            {
                var module = new VbaModuleDiffViewModel(d) { IsReviewMode = _isReviewMode };
                module.SetFilter(_showAdded, _showModified, _showRemoved);
                VbaModules.Add(module);
            }

            RebuildDisplayVbaModules();

            SelectedVbaModule = DisplayVbaModules.Count > 0 ? DisplayVbaModules[0] : null;
            HasVba = VbaModules.Count > 0;

            IsLoaded = true;

            if (recentEntry != null)
            {
                UpdateRecentEntries(recentEntry);
            }
        }
        catch (ExcelReadException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"予期しないエラーが発生しました:\n{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>バックグラウンドスレッドで実行する重い処理（読み込み＋差分計算）。</summary>
    private static LoadResult Compute(string leftPath, string rightPath)
    {
        var left = ExcelReader.Read(leftPath);
        var right = ExcelReader.Read(rightPath);
        var sheetDiffs = DiffEngine.Compare(left, right);

        // VBA（.xlsm のみ。失敗・無しでもデータ・数式ビューは継続）。
        IReadOnlyList<VbaModuleDiff> vbaDiffs = System.Array.Empty<VbaModuleDiff>();
        try
        {
            var beforeVba = VbaProjectReader.Read(leftPath);
            var afterVba = VbaProjectReader.Read(rightPath);
            if (beforeVba.Count > 0 || afterVba.Count > 0)
            {
                vbaDiffs = VbaDiffEngine.Compare(beforeVba, afterVba);
            }
        }
        catch
        {
            // VBA 解析失敗は致命的ではない。
        }

        return new LoadResult(sheetDiffs, vbaDiffs);
    }

    /// <summary>パスの短い表示名（ファイル名）を返す。フルパスは Tooltip 等で使用。</summary>
    public static string ToDisplayName(string path)
        => string.IsNullOrEmpty(path) ? "(未選択)" : Path.GetFileName(path);

    /// <summary>
    /// 新規エントリを履歴の先頭に追加して永続化する。同一比較は先頭へ移動、上限を超えた古い分は削除。
    /// </summary>
    private void UpdateRecentEntries(RecentEntry entry)
    {
        var list = new List<RecentEntry>(RecentEntries);
        RecentsService.Add(list, entry);
        RecentsService.Save(list);

        // ObservableCollection を List の順序に合わせて更新する。
        RecentEntries.Clear();
        foreach (var e in list)
        {
            RecentEntries.Add(e);
        }
    }

    /// <summary>指定エントリを履歴から削除して永続化する（Git 復元失敗時など）。</summary>
    public void RemoveRecentEntry(RecentEntry entry)
    {
        if (!RecentEntries.Remove(entry))
        {
            return;
        }

        RecentsService.Save(RecentEntries.ToList());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
