using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ExcelGitDiffViewer.Models;
using OpenMcdf;

namespace ExcelGitDiffViewer.Services;

/// <summary>
/// .xlsm の vbaProject.bin から VBA モジュールのソースを抽出する（仕様 §3.2）。
/// 手順: OOXML(zip) → xl/vbaProject.bin → OpenMcdf で CFB を開く →
/// VBA ストレージ配下の各ストリームから MS-OVBA 圧縮されたソースを展開する。
/// dir ストリームの厳密な解析は避け、各ストリーム内で圧縮コンテナを走査して
/// "Attribute VB_Name" を含むソースを採用するヒューリスティックを用いる。
/// </summary>
public static class VbaProjectReader
{
    static VbaProjectReader()
    {
        // cp932 等の ANSI コードページを使えるように登録。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static Encoding Ansi
    {
        get
        {
            try
            {
                return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
            }
            catch
            {
                return Encoding.Latin1;
            }
        }
    }

    /// <summary>
    /// 指定ファイルから VBA モジュール一覧を返す。VBA が無い／OOXML でない場合は空。
    /// </summary>
    public static IReadOnlyList<VbaModule> Read(string filePath)
    {
        // OOXML(zip) コンテナのみ対象（.xlsm）。.xls の VBA は対象外（仕様 §3.2）。
        if (FileFormatDetector.Detect(filePath) != ExcelFormat.Xlsx)
        {
            return System.Array.Empty<VbaModule>();
        }

        byte[]? bin = ExtractVbaBin(filePath);
        if (bin == null)
        {
            return System.Array.Empty<VbaModule>();
        }

        try
        {
            return ExtractModules(bin);
        }
        catch
        {
            // 解析失敗時は空（データ・数式ビューは引き続き利用可能）。
            return System.Array.Empty<VbaModule>();
        }
    }

    private static byte[]? ExtractVbaBin(string filePath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            var entry = zip.GetEntry("xl/vbaProject.bin");
            if (entry == null)
            {
                return null;
            }

            using var es = entry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<VbaModule> ExtractModules(byte[] bin)
    {
        var modules = new List<VbaModule>();
        string tempPath = Path.Combine(Path.GetTempPath(), $"egdv_vba_{System.Guid.NewGuid():N}.bin");
        File.WriteAllBytes(tempPath, bin);
        try
        {
            using var root = RootStorage.OpenRead(tempPath);
            Storage vbaStorage;
            try
            {
                vbaStorage = root.OpenStorage("VBA");
            }
            catch
            {
                return modules;
            }

            foreach (var entry in vbaStorage.EnumerateEntries())
            {
                if (entry.Type != EntryType.Stream)
                {
                    continue;
                }

                string name = entry.Name;
                if (name is "dir" or "_VBA_PROJECT" || name.StartsWith("__SRP_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                byte[] streamBytes = ReadStream(vbaStorage, name);
                if (TryExtractSource(streamBytes, out string source))
                {
                    modules.Add(new VbaModule(ExtractModuleName(source, fallback: name), source));
                }
            }
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* 無視 */ }
        }

        modules.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
        return modules;
    }

    private static byte[] ReadStream(Storage storage, string name)
    {
        using var stream = storage.OpenStream(name);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// ストリーム内で圧縮コンテナ（先頭 0x01）を走査し、展開結果が VBA ソースなら採用する。
    /// </summary>
    private static bool TryExtractSource(byte[] streamBytes, out string source)
    {
        source = string.Empty;
        var ansi = Ansi;

        for (int i = 0; i < streamBytes.Length; i++)
        {
            if (streamBytes[i] != 0x01)
            {
                continue;
            }

            // 直後の2バイトが CompressedChunkHeader（signature=0b011）か簡易チェック。
            if (i + 2 >= streamBytes.Length)
            {
                break;
            }

            int header = streamBytes[i + 1] | (streamBytes[i + 2] << 8);
            if (((header >> 12) & 0x07) != 0b011)
            {
                continue;
            }

            try
            {
                byte[] decompressed = VbaDecompression.Decompress(streamBytes, i);
                string text = ansi.GetString(decompressed);
                if (text.Contains("Attribute VB_Name", System.StringComparison.Ordinal))
                {
                    source = NormalizeNewlines(text);
                    return true;
                }
            }
            catch
            {
                // この位置は本物のコンテナではなかった。次の候補へ。
            }
        }

        return false;
    }

    private static readonly Regex VbNameRegex =
        new("Attribute\\s+VB_Name\\s*=\\s*\"([^\"]*)\"", RegexOptions.Compiled);

    private static string ExtractModuleName(string source, string fallback)
    {
        var m = VbNameRegex.Match(source);
        return m.Success ? m.Groups[1].Value : fallback;
    }

    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
