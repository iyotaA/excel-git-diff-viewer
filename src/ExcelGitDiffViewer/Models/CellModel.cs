namespace ExcelGitDiffViewer.Models;

/// <summary>
/// 表示用の1セル。値（表示文字列）・数式・差分種別を保持する。
/// 差分付与は <see cref="Services.DiffEngine"/> が行うため可変。
/// </summary>
public sealed class CellModel
{
    /// <summary>0始まりの行インデックス（対向側と揃えた論理行ではなく、元シート上の行）。</summary>
    public int RowIndex { get; }

    /// <summary>0始まりの列インデックス。</summary>
    public int ColumnIndex { get; }

    /// <summary>セルの表示文字列（値）。空セルは空文字。</summary>
    public string Display { get; }

    /// <summary>数式文字列（"=" 始まり）。数式が無ければ null。</summary>
    public string? Formula { get; }

    /// <summary>差分種別。初期値は <see cref="DiffKind.Unchanged"/>。</summary>
    public DiffKind Diff { get; set; } = DiffKind.Unchanged;

    public CellModel(int rowIndex, int columnIndex, string display, string? formula = null)
    {
        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
        Display = display ?? string.Empty;
        Formula = string.IsNullOrEmpty(formula) ? null : formula;
    }

    /// <summary>表示文字列が空（空白のみ含む）かどうか。</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Display);

    /// <summary>数式を持つか（数式インジケータ表示に使用）。</summary>
    public bool HasFormula => Formula != null;
}
