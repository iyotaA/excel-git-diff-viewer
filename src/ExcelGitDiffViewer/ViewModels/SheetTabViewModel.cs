using System.Collections.Generic;
using ExcelGitDiffViewer.Models;

namespace ExcelGitDiffViewer.ViewModels;

/// <summary>
/// シートタブ1枚分の表示用 ViewModel。タブ見出しと左右の行コレクションを提供する。
/// </summary>
public sealed class SheetTabViewModel
{
    private readonly SheetDiffModel _diff;

    public SheetTabViewModel(SheetDiffModel diff)
    {
        _diff = diff;
    }

    public string Name => _diff.Name;

    /// <summary>タブ見出し（状態マーカー付き, 仕様 §3.5 / §4）。</summary>
    public string Header => _diff.Status switch
    {
        SheetChangeStatus.Added => $"{_diff.Name}（追加）",
        SheetChangeStatus.Removed => $"{_diff.Name}（削除）",
        SheetChangeStatus.Modified => $"{_diff.Name}（変更あり）",
        _ => _diff.Name,
    };

    public IReadOnlyList<RowModel> LeftRows => _diff.LeftRows;

    public IReadOnlyList<RowModel> RightRows => _diff.RightRows;

    public int ColumnCount => _diff.ColumnCount;
}
