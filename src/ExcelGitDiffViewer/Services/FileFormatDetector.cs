using System.IO;
using ExcelGitDiffViewer.Models;

namespace ExcelGitDiffViewer.Services;

/// <summary>
/// ファイル先頭のマジックバイトで Excel 形式を判定する（仕様 §3.3）。
/// git difftool が渡す一時ファイルは拡張子を失う場合があるため、拡張子に依存しない。
/// </summary>
public static class FileFormatDetector
{
    // .xlsx / .xlsm （ZIP コンテナ） : 50 4B 03 04 "PK.."
    private static readonly byte[] ZipSignature = { 0x50, 0x4B, 0x03, 0x04 };

    // .xls （OLE2 複合ファイル） : D0 CF 11 E0 A1 B1 1A E1
    private static readonly byte[] Ole2Signature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

    /// <summary>
    /// 指定パスのファイル形式を判定する。読み取り不能・空ファイル等は <see cref="ExcelFormat.Unknown"/>。
    /// </summary>
    public static ExcelFormat Detect(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return ExcelFormat.Unknown;
        }

        Span<byte> header = stackalloc byte[8];
        int read;
        using (var stream = File.OpenRead(filePath))
        {
            read = stream.Read(header);
        }

        if (read >= ZipSignature.Length && StartsWith(header, ZipSignature))
        {
            return ExcelFormat.Xlsx;
        }

        if (read >= Ole2Signature.Length && StartsWith(header, Ole2Signature))
        {
            return ExcelFormat.Xls;
        }

        return ExcelFormat.Unknown;
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        for (int i = 0; i < signature.Length; i++)
        {
            if (data[i] != signature[i])
            {
                return false;
            }
        }

        return true;
    }
}
