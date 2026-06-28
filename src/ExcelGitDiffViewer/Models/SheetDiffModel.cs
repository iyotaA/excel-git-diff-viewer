using System.Collections.Generic;

namespace ExcelGitDiffViewer.Models;

/// <summary>
/// シート単位の差分状態（仕様 §3.5 のうち MVP で扱う範囲）。
/// </summary>
public enum SheetChangeStatus
{
    /// <summary>両側に存在し、セル差分なし。</summary>
    Unchanged,

    /// <summary>両側に存在し、セル差分あり。</summary>
    Modified,

    /// <summary>右（変更後）にのみ存在＝追加されたシート。</summary>
    Added,

    /// <summary>左（変更前）にのみ存在＝削除されたシート。</summary>
    Removed,
}

/// <summary>
/// 1シート分の左右差分結果。左右それぞれの行モデルと、列数・状態を保持する。
/// </summary>
public sealed class SheetDiffModel
{
    /// <summary>シート名（左右で同名のもの。片側のみの場合は存在する側の名前）。</summary>
    public string Name { get; }

    /// <summary>シート状態。</summary>
    public SheetChangeStatus Status { get; }

    /// <summary>左（変更前）の行群。</summary>
    public IReadOnlyList<RowModel> LeftRows { get; }

    /// <summary>右（変更後）の行群。</summary>
    public IReadOnlyList<RowModel> RightRows { get; }

    /// <summary>左右で揃えた表示列数。</summary>
    public int ColumnCount { get; }

    public SheetDiffModel(
        string name,
        SheetChangeStatus status,
        IReadOnlyList<RowModel> leftRows,
        IReadOnlyList<RowModel> rightRows,
        int columnCount)
    {
        Name = name;
        Status = status;
        LeftRows = leftRows;
        RightRows = rightRows;
        ColumnCount = columnCount;
    }
}
