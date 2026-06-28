namespace ExcelGitDiffViewer.Services;

/// <summary>
/// Excel 読み込みに失敗したことを表す例外（未対応形式・破損・暗号化・空ファイル等, 仕様 §7）。
/// メッセージはそのまま UI に提示できる日本語とする。
/// </summary>
public sealed class ExcelReadException : Exception
{
    public ExcelReadException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
