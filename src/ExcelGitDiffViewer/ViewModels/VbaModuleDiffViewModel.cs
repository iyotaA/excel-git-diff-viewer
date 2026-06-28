using System.Collections.Generic;
using ExcelGitDiffViewer.Models;

namespace ExcelGitDiffViewer.ViewModels;

/// <summary>VBA モジュール差分の表示用 ViewModel（モジュール一覧の1項目）。</summary>
public sealed class VbaModuleDiffViewModel
{
    private readonly VbaModuleDiff _diff;

    public VbaModuleDiffViewModel(VbaModuleDiff diff)
    {
        _diff = diff;
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
}
