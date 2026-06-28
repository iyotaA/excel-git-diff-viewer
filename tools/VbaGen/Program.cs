using System.IO.Compression;
using System.Text;
using OpenMcdf;

// 検証用: before.xlsx / after.xlsx をコピーして .xlsm を作り、VBA を含む vbaProject.bin を注入する。
// vbaProject.bin は OpenMcdf で CFB を作成し、各モジュールに MS-OVBA 圧縮（全リテラル）したソースを格納する。

string dir = args.Length > 0 ? args[0] : ".";

var before = new Dictionary<string, string>
{
    ["Module0"] = "Attribute VB_Name = \"Module0\"\r\nSub Old()\r\n    ' removed module\r\n    MsgBox \"old\"\r\nEnd Sub\r\n",
    ["Module1"] = "Attribute VB_Name = \"Module1\"\r\nSub Hello()\r\n    MsgBox \"Hello\"\r\n    Dim x As Integer\r\n    x = 1\r\nEnd Sub\r\n",
};

var after = new Dictionary<string, string>
{
    ["Module1"] = "Attribute VB_Name = \"Module1\"\r\nSub Hello()\r\n    MsgBox \"Hello, World\"\r\n    Dim x As Integer\r\n    x = 2\r\n    Debug.Print x\r\nEnd Sub\r\n",
    ["Module2"] = "Attribute VB_Name = \"Module2\"\r\nSub Added()\r\n    ' added module\r\n    Range(\"A1\").Value = 100\r\nEnd Sub\r\n",
};

BuildXlsm(Path.Combine(dir, "before.xlsx"), Path.Combine(dir, "before.xlsm"), before);
BuildXlsm(Path.Combine(dir, "after.xlsx"), Path.Combine(dir, "after.xlsm"), after);
Console.WriteLine($"VBA入り .xlsm 生成完了: {Path.GetFullPath(dir)}");

static void BuildXlsm(string srcXlsx, string dstXlsm, Dictionary<string, string> modules)
{
    File.Copy(srcXlsx, dstXlsm, overwrite: true);

    byte[] bin = BuildVbaProjectBin(modules);

    using var zip = ZipFile.Open(dstXlsm, ZipArchiveMode.Update);

    // vbaProject.bin を追加。
    zip.GetEntry("xl/vbaProject.bin")?.Delete();
    using (var es = zip.CreateEntry("xl/vbaProject.bin").Open())
    {
        es.Write(bin, 0, bin.Length);
    }

    // [Content_Types].xml に bin 拡張子の Default を追加（OPC 検証を通すため）。
    PatchContentTypes(zip);
}

static void PatchContentTypes(ZipArchive zip)
{
    var ctEntry = zip.GetEntry("[Content_Types].xml");
    if (ctEntry == null)
    {
        return;
    }

    string xml;
    using (var rs = new StreamReader(ctEntry.Open(), Encoding.UTF8))
    {
        xml = rs.ReadToEnd();
    }

    if (xml.Contains("Extension=\"bin\""))
    {
        return;
    }

    string insert = "<Default Extension=\"bin\" ContentType=\"application/vnd.ms-office.vbaProject\"/>";
    xml = xml.Replace("</Types>", insert + "</Types>");

    ctEntry.Delete();
    using var ws = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open(), new UTF8Encoding(false));
    ws.Write(xml);
}

static byte[] BuildVbaProjectBin(Dictionary<string, string> modules)
{
    string tmp = Path.Combine(Path.GetTempPath(), $"vbagen_{Guid.NewGuid():N}.bin");
    try
    {
        using (var root = RootStorage.Create(tmp))
        {
            var vba = root.CreateStorage("VBA");
            // ダミーの dir / _VBA_PROJECT（リーダーはスキップする）。
            WriteStream(vba, "dir", new byte[] { 0x01 });
            WriteStream(vba, "_VBA_PROJECT", new byte[] { 0xCC, 0x61 });
            foreach (var (name, source) in modules)
            {
                byte[] container = CompressAllLiterals(Encoding.ASCII.GetBytes(source));
                WriteStream(vba, name, container);
            }
        }

        return File.ReadAllBytes(tmp);
    }
    finally
    {
        try { File.Delete(tmp); } catch { }
    }
}

static void WriteStream(Storage storage, string name, byte[] data)
{
    using var s = storage.CreateStream(name);
    s.Write(data, 0, data.Length);
}

// MS-OVBA CompressedContainer を「全リテラルトークン」で生成（圧縮はしないが仕様準拠）。
static byte[] CompressAllLiterals(byte[] source)
{
    using var ms = new MemoryStream();
    ms.WriteByte(0x01); // signature

    const int maxDecompressedPerChunk = 3500; // 全リテラルでも圧縮後 <= 4096 に収まるサイズ
    int offset = 0;
    while (offset < source.Length)
    {
        int take = Math.Min(maxDecompressedPerChunk, source.Length - offset);

        // チャンクの圧縮データ（8リテラルごとに flagByte=0x00 を前置）。
        using var chunk = new MemoryStream();
        int i = 0;
        while (i < take)
        {
            chunk.WriteByte(0x00); // 8トークン分のフラグ（すべてリテラル）
            for (int b = 0; b < 8 && i < take; b++)
            {
                chunk.WriteByte(source[offset + i]);
                i++;
            }
        }

        byte[] chunkData = chunk.ToArray();
        int chunkSizeField = chunkData.Length - 1; // = (2 + dataLen) - 3
        int header = (chunkSizeField & 0x0FFF) | (0b011 << 12) | (1 << 15); // sig=011, compressed=1
        ms.WriteByte((byte)(header & 0xFF));
        ms.WriteByte((byte)((header >> 8) & 0xFF));
        ms.Write(chunkData, 0, chunkData.Length);

        offset += take;
    }

    return ms.ToArray();
}
