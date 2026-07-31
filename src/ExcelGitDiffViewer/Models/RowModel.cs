using System.Collections.Generic;

namespace ExcelGitDiffViewer.Models;

/// <summary>
/// DataGrid の1行（行アライメント後の論理行）。
/// 対向側に対応行が無いギャップ行では <see cref="IsGap"/> が true、<see cref="RowNumber"/> は null。
/// </summary>
public sealed class RowModel
{
    /// <summary>左右で揃えた論理行インデックス（0始まり）。左右で同じ値になる。</summary>
    public int AlignedIndex { get; }

    /// <summary>元シート上の行番号（1始まり）。ギャップ行では null（表示は空）。</summary>
    public int? RowNumber { get; }

    /// <summary>対向側に対応行が無いギャップ行か。</summary>
    public bool IsGap { get; }

    /// <summary>列順に並んだセル群。</summary>
    public IReadOnlyList<CellModel> Cells { get; }

    /// <summary>
    /// この行を表示するのに要する行数（セル内改行の最大数＋1、最小 1）。
    /// 行高の算出に使う。左右で行ピクセル高を一致させる必要があるため、
    /// 対応する左右の RowModel には「改行の多い側」の同じ値を与える。
    /// </summary>
    public int DisplayLineCount { get; }

    public RowModel(int alignedIndex, int? rowNumber, bool isGap, IReadOnlyList<CellModel> cells, int displayLineCount)
    {
        AlignedIndex = alignedIndex;
        RowNumber = rowNumber;
        IsGap = isGap;
        Cells = cells;
        DisplayLineCount = displayLineCount < 1 ? 1 : displayLineCount;
    }
}
