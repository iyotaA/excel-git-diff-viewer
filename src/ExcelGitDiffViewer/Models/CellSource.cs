namespace ExcelGitDiffViewer.Models;

/// <summary>
/// Excel から読み取ったセルの生データ（表示値と数式文字列）。
/// 数式が無いセルでは <see cref="Formula"/> は null。
/// </summary>
public readonly struct CellSource
{
    /// <summary>表示値（数式セルはキャッシュされた計算結果）。</summary>
    public string Value { get; }

    /// <summary>数式文字列（"=" 始まり）。数式が無ければ null。</summary>
    public string? Formula { get; }

    public CellSource(string value, string? formula)
    {
        Value = value ?? string.Empty;
        Formula = string.IsNullOrEmpty(formula) ? null : formula;
    }

    public static readonly CellSource Empty = new(string.Empty, null);

    public bool IsEmpty => string.IsNullOrEmpty(Value) && Formula == null;

    /// <summary>値・数式が等しいか（差分判定に使用）。</summary>
    public bool ContentEquals(in CellSource other)
        => string.Equals(Value, other.Value, StringComparison.Ordinal)
           && string.Equals(Formula ?? string.Empty, other.Formula ?? string.Empty, StringComparison.Ordinal);
}
