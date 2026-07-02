using System.Collections.Generic;
using System.ComponentModel;
using ExcelGitDiffViewer.Models;

namespace ExcelGitDiffViewer.ViewModels;

/// <summary>
/// VBA モジュール差分の表示用 ViewModel（モジュール一覧の1項目）。
/// レビューモード（差分のみ表示）では、表示用の行コレクションを差分行だけに絞り込む。
/// </summary>
public sealed class VbaModuleDiffViewModel : INotifyPropertyChanged
{
    private readonly VbaModuleDiff _diff;

    // レビューモード用に差分行だけを事前抽出したコレクション。
    private readonly IReadOnlyList<VbaLineRow> _diffLines;

    private bool _isReviewMode;

    public VbaModuleDiffViewModel(VbaModuleDiff diff)
    {
        _diff = diff;

        var diffLines = new List<VbaLineRow>();
        foreach (var line in diff.Lines)
        {
            if (line.LeftKind != DiffKind.Unchanged || line.RightKind != DiffKind.Unchanged)
            {
                diffLines.Add(line);
            }
        }

        _diffLines = diffLines;
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

    /// <summary>右ペインの差分行にバインドする行（レビューモード時は差分行のみ）。</summary>
    public IReadOnlyList<VbaLineRow> DisplayLines => _isReviewMode ? _diffLines : _diff.Lines;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
