using System.Collections.Generic;
using System.ComponentModel;
using ExcelGitDiffViewer.Models;

namespace ExcelGitDiffViewer.ViewModels;

/// <summary>
/// VBA モジュール差分の表示用 ViewModel（モジュール一覧の1項目）。
/// レビューモード（差分のみ表示）では、表示用の行コレクションを差分行だけに絞り込む。
/// さらに「追加 / 変更 / 削除」フィルタ（A-5）で内訳を切り替えられる。
/// </summary>
public sealed class VbaModuleDiffViewModel : INotifyPropertyChanged
{
    /// <summary>差分行の種別（A-5 フィルタ用）。</summary>
    private enum RowKind
    {
        Modified,
        Added,   // 左が Gap（右にのみ行がある）
        Removed, // 右が Gap（左にのみ行がある）
    }

    private readonly VbaModuleDiff _diff;

    // レビューモード用に差分行だけを事前抽出したコレクション。
    private readonly IReadOnlyList<VbaLineRow> _diffLines;
    // 各差分行の分類（インデックスは _diffLines と対応）。
    private readonly IReadOnlyList<RowKind> _diffLineKinds;

    // フィルタ後の差分行。既定は全種類 ON なので _diffLines と一致する。
    private IReadOnlyList<VbaLineRow> _filteredLines;

    private bool _isReviewMode;

    public VbaModuleDiffViewModel(VbaModuleDiff diff)
    {
        _diff = diff;

        var diffLines = new List<VbaLineRow>();
        var kinds = new List<RowKind>();
        foreach (var line in diff.Lines)
        {
            if (line.LeftKind != DiffKind.Unchanged || line.RightKind != DiffKind.Unchanged)
            {
                diffLines.Add(line);
                kinds.Add(ClassifyLine(line));
            }
        }

        _diffLines = diffLines;
        _diffLineKinds = kinds;
        _filteredLines = _diffLines;
    }

    public string Name => _diff.Name;

    public IReadOnlyList<VbaLineRow> Lines => _diff.Lines;

    /// <summary>モジュール一覧の見出し（状態マーカー付き）。</summary>
    public string Header => _diff.Status switch
    {
        VbaModuleStatus.Added => $"{_diff.Name}（追加）",
        VbaModuleStatus.Removed => $"{_diff.Name}（削除）",
        VbaModuleStatus.Modified => $"{_diff.Name}（変更あり）",
        _ => _diff.Name,
    };

    /// <summary>変更を含むモジュールか（Unchanged 以外）。モジュール一覧のレビュー絞り込みに使う。</summary>
    public bool HasDiff => _diff.Status != VbaModuleStatus.Unchanged;

    /// <summary>差分行の件数（レビューモードの ON/OFF に依らない固定値）。</summary>
    public int DiffLineCount => _diffLines.Count;

    /// <summary>レビューモード（差分のみ表示）か。切替時に表示用コレクションを差し替える。</summary>
    public bool IsReviewMode
    {
        get => _isReviewMode;
        set
        {
            if (_isReviewMode == value)
            {
                return;
            }

            _isReviewMode = value;
            OnPropertyChanged(nameof(DisplayLines));
        }
    }

    /// <summary>右ペインの差分行にバインドする行（レビューモード時はフィルタ後、OFF 時は全行）。</summary>
    public IReadOnlyList<VbaLineRow> DisplayLines => _isReviewMode ? _filteredLines : _diff.Lines;

    /// <summary>差分行のフィルタを更新する（A-5）。レビューモード ON 時にのみ表示に反映される。</summary>
    public void SetFilter(bool showAdded, bool showModified, bool showRemoved)
    {
        if (showAdded && showModified && showRemoved)
        {
            if (!ReferenceEquals(_filteredLines, _diffLines))
            {
                _filteredLines = _diffLines;
                if (_isReviewMode)
                {
                    OnPropertyChanged(nameof(DisplayLines));
                }
            }

            return;
        }

        var filtered = new List<VbaLineRow>();
        for (int i = 0; i < _diffLineKinds.Count; i++)
        {
            bool keep = _diffLineKinds[i] switch
            {
                RowKind.Added => showAdded,
                RowKind.Modified => showModified,
                RowKind.Removed => showRemoved,
                _ => true,
            };
            if (keep)
            {
                filtered.Add(_diffLines[i]);
            }
        }

        _filteredLines = filtered;
        if (_isReviewMode)
        {
            OnPropertyChanged(nameof(DisplayLines));
        }
    }

    /// <summary>1行を「追加 / 削除 / 変更」に分類する。VBA 行は左右のいずれか片方が Gap の場合が分かりやすい。</summary>
    private static RowKind ClassifyLine(VbaLineRow line)
    {
        if (line.LeftKind == DiffKind.Gap)
        {
            return RowKind.Added;
        }

        if (line.RightKind == DiffKind.Gap)
        {
            return RowKind.Removed;
        }

        return RowKind.Modified;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
