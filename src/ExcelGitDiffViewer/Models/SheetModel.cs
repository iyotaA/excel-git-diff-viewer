using System.Collections.Generic;

namespace ExcelGitDiffViewer.Models;

/// <summary>
/// 1シート分の読み込み結果。値・数式を矩形（最終行 x 最終列）に正規化して保持する。
/// </summary>
public sealed class SheetModel
{
    /// <summary>シート名。</summary>
    public string Name { get; }

    /// <summary>行数（0行のこともある）。</summary>
    public int RowCount { get; }

    /// <summary>列数（0列のこともある）。</summary>
    public int ColumnCount { get; }

    /// <summary>
    /// [row][col] でアクセスするセル（値＋数式）。矩形に正規化済み（欠損は <see cref="CellSource.Empty"/>）。
    /// </summary>
    public IReadOnlyList<IReadOnlyList<CellSource>> Cells { get; }

    public SheetModel(string name, IReadOnlyList<IReadOnlyList<CellSource>> cells, int rowCount, int columnCount)
    {
        Name = name;
        Cells = cells;
        RowCount = rowCount;
        ColumnCount = columnCount;
    }

    /// <summary>
    /// 指定座標のセルを返す。範囲外は <see cref="CellSource.Empty"/>。
    /// </summary>
    public CellSource CellAt(int row, int col)
    {
        if (row < 0 || row >= RowCount || col < 0 || col >= ColumnCount)
        {
            return CellSource.Empty;
        }

        return Cells[row][col];
    }
}
