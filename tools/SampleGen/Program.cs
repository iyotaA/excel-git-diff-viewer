using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

// 検証用サンプル before.xlsx / after.xlsx を生成する。
// 値変更・追加行・削除行・列増減・複数シート・シート追加を含む。

string outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);

WriteBefore(Path.Combine(outDir, "before.xlsx"));
WriteAfter(Path.Combine(outDir, "after.xlsx"));

// .xls（HSSF）形式の最小ペアも生成して .xls 経路を検証する。
WriteXls(Path.Combine(outDir, "before.xls"), "100");
WriteXls(Path.Combine(outDir, "after.xls"), "150");
Console.WriteLine($"生成完了: {Path.GetFullPath(outDir)}");

static void WriteXls(string path, string tokyoJan)
{
    var wb = new HSSFWorkbook();
    var s = wb.CreateSheet("売上");
    Set(s, 0, "項目", "1月", "2月");
    Set(s, 1, "東京", tokyoJan, "120");
    Set(s, 2, "大阪", "80", "90");
    Save(wb, path);
}

static void WriteBefore(string path)
{
    var wb = new XSSFWorkbook();

    // 売上: 数式列(合計) を持ち、値変更・行挿入・数式変更を after で行う
    var s1 = wb.CreateSheet("売上");
    Set(s1, 0, "項目", "1月", "2月", "合計");
    SetWithFormula(s1, 1, "東京", "100", "120", "B2+C2");
    SetWithFormula(s1, 2, "大阪", "80", "90", "B3+C3");
    SetWithFormula(s1, 3, "名古屋", "60", "70", "B4+C4");
    SetWithFormula(s1, 4, "総計", null, null, "SUM(D2:D4)",
        bFormula: "SUM(B2:B4)", cFormula: "SUM(C2:C4)");

    // マスタ: 変更なし
    var s2 = wb.CreateSheet("マスタ");
    Set(s2, 0, "コード", "名称");
    Set(s2, 1, "A01", "りんご");
    Set(s2, 2, "A02", "みかん");

    // 旧データ: after では削除されるシート
    var s3 = wb.CreateSheet("旧データ");
    Set(s3, 0, "x", "y");
    Set(s3, 1, "1", "2");

    Save(wb, path);
}

static void WriteAfter(string path)
{
    var wb = new XSSFWorkbook();

    // 売上: 東京 100→150 変更、福岡 行を挿入、名古屋 70→75 変更、総計 SUM 範囲変更
    var s1 = wb.CreateSheet("売上");
    Set(s1, 0, "項目", "1月", "2月", "合計");
    SetWithFormula(s1, 1, "東京", "150", "120", "B2+C2"); // 100→150
    SetWithFormula(s1, 2, "大阪", "80", "90", "B3+C3");
    SetWithFormula(s1, 3, "福岡", "40", "50", "B4+C4");   // 挿入行
    SetWithFormula(s1, 4, "名古屋", "60", "75", "B5+C5"); // 70→75
    SetWithFormula(s1, 5, "総計", null, null, "SUM(D2:D5)",
        bFormula: "SUM(B2:B5)", cFormula: "SUM(C2:C5)"); // 範囲変更

    // マスタ: 変更なし
    var s2 = wb.CreateSheet("マスタ");
    Set(s2, 0, "コード", "名称");
    Set(s2, 1, "A01", "りんご");
    Set(s2, 2, "A02", "みかん");

    // 新データ: 追加されたシート
    var s4 = wb.CreateSheet("新データ");
    Set(s4, 0, "p", "q");
    Set(s4, 1, "10", "20");

    Save(wb, path);
}

// 列A=ラベル, B/C=数値(またはnull), D=数式(またはnull)。総計行は b/c/dFormula で数式指定。
static void SetWithFormula(ISheet sheet, int rowIndex, string label, string? b, string? c, string? dFormula,
    string? bFormula = null, string? cFormula = null)
{
    var row = sheet.CreateRow(rowIndex);
    row.CreateCell(0).SetCellValue(label);
    SetCell(row, 1, b, bFormula);
    SetCell(row, 2, c, cFormula);
    SetCell(row, 3, null, dFormula);
}

static void SetCell(IRow row, int col, string? value, string? formula)
{
    var cell = row.CreateCell(col);
    if (formula != null)
    {
        cell.SetCellFormula(formula);
    }
    else if (value != null)
    {
        // 数値は double で格納（文字列だと SUM が無視してしまうため）。
        if (double.TryParse(value, out var num))
        {
            cell.SetCellValue(num);
        }
        else
        {
            cell.SetCellValue(value);
        }
    }
}

static void Set(ISheet sheet, int rowIndex, params string[] values)
{
    var row = sheet.CreateRow(rowIndex);
    for (int c = 0; c < values.Length; c++)
    {
        row.CreateCell(c).SetCellValue(values[c]);
    }
}

static void Save(IWorkbook wb, string path)
{
    // 数式のキャッシュ値を計算しておく（ビューア側は計算結果を表示するため）。
    if (wb is XSSFWorkbook)
    {
        NPOI.XSSF.UserModel.XSSFFormulaEvaluator.EvaluateAllFormulaCells(wb);
    }

    using var fs = File.Create(path);
    wb.Write(fs);
}
