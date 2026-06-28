namespace ExcelGitDiffViewer.Models;

/// <summary>
/// マジックバイトで判定した Excel ファイル形式（仕様 §3.3）。
/// </summary>
public enum ExcelFormat
{
    /// <summary>未対応・判定不能（解析対象外）。</summary>
    Unknown,

    /// <summary>OOXML（.xlsx / .xlsm）。先頭が "PK.." (50 4B 03 04)。</summary>
    Xlsx,

    /// <summary>OLE2 複合ファイル（.xls）。先頭が D0 CF 11 E0 A1 B1 1A E1。</summary>
    Xls,
}
