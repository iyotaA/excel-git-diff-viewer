using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ExcelGitDiffViewer.Models;
using ExcelGitDiffViewer.Services;

namespace ExcelGitDiffViewer.ViewModels;

/// <summary>表示モード（ホーム / データ・数式ビュー / VBAコードビュー, 仕様 §4）。</summary>
public enum ViewMode
{
    Home,
    DataFormula,
    Vba,
}

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
            }
        }
    }

    /// <summary>シートタブ群。</summary>
    public ObservableCollection<SheetTabViewModel> Sheets { get; } = new();

    /// <summary>現在選択中のシート。</summary>
    public SheetTabViewModel? SelectedSheet
    {
        get => _selectedSheet;
        set => SetField(ref _selectedSheet, value);
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

                RebuildDisplayVbaModules();

                // フィルタで選択中のモジュールが消えた場合は先頭に再選択する。
                if (SelectedVbaModule == null || !DisplayVbaModules.Contains(SelectedVbaModule))
                {
                    SelectedVbaModule = DisplayVbaModules.Count > 0 ? DisplayVbaModules[0] : null;
                }
            }
        }
    }

    /// <summary>VBA モジュール差分一覧（全件保持）。</summary>
    public ObservableCollection<VbaModuleDiffViewModel> VbaModules { get; } = new();

    /// <summary>モジュール一覧に表示する VBA モジュール（レビュー ON 時は変更ありのみ）。</summary>
    public ObservableCollection<VbaModuleDiffViewModel> DisplayVbaModules { get; } = new();

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

    public void ShowHomeView() => CurrentView = ViewMode.Home;

    public void ShowDataView() => CurrentView = ViewMode.DataFormula;

    public void ShowVbaView()
    {
        if (HasVba)
        {
            CurrentView = ViewMode.Vba;
        }
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

    /// <summary>バックグラウンドで計算した差分結果（UI スレッドへ受け渡す中間データ）。</summary>
    private sealed record LoadResult(
        IReadOnlyList<SheetDiffModel> Sheets,
        IReadOnlyList<VbaModuleDiff> VbaModules);

    /// <summary>
    /// 左右2ファイルを非同期に読み込み差分を計算する。重い処理は別スレッドで実行し、
    /// UI フリーズを防ぐ。失敗時は <see cref="ErrorMessage"/> を設定する。
    /// </summary>
    /// <param name="leftLabel">情報バー表示用のラベル（省略時はパス）。git 比較時のコミット名等に使用。</param>
    public async Task LoadAsync(string leftPath, string rightPath, string? leftLabel = null, string? rightLabel = null)
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

        try
        {
            var result = await Task.Run(() => Compute(leftPath, rightPath)).ConfigureAwait(true);

            foreach (var diff in result.Sheets)
            {
                Sheets.Add(new SheetTabViewModel(diff) { IsReviewMode = _isReviewMode });
            }

            SelectedSheet = Sheets.Count > 0 ? Sheets[0] : null;

            foreach (var d in result.VbaModules)
            {
                VbaModules.Add(new VbaModuleDiffViewModel(d) { IsReviewMode = _isReviewMode });
            }

            RebuildDisplayVbaModules();

            SelectedVbaModule = DisplayVbaModules.Count > 0 ? DisplayVbaModules[0] : null;
            HasVba = VbaModules.Count > 0;

            IsLoaded = true;
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
