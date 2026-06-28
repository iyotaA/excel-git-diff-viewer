using System.Collections.Generic;
using System.IO;
using ExcelGitDiffViewer.Models;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace ExcelGitDiffViewer.Services;

/// <summary>
/// NPOI を用いて Excel の「値」を抽出する（仕様 §3.1）。
/// MVP では数式セルはキャッシュされた計算結果を表示する（数式文字列は Phase 2a）。
/// </summary>
public static class ExcelReader
{
    /// <summary>
    /// 指定ファイルを読み込み <see cref="WorkbookModel"/> を構築する。
    /// 失敗時は <see cref="ExcelReadException"/> を投げる。
    /// </summary>
    public static WorkbookModel Read(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ExcelReadException("ファイルパスが指定されていません。");
        }

        if (!File.Exists(filePath))
        {
            throw new ExcelReadException($"ファイルが見つかりません:\n{filePath}");
        }

        var format = FileFormatDetector.Detect(filePath);
        if (format == ExcelFormat.Unknown)
        {
            throw new ExcelReadException(
                "対応していないファイル形式です（Excel ファイルではない可能性があります）。\n" +
                ".xlsx / .xlsm / .xls のみ解析できます。");
        }

        IWorkbook workbook;
        try
        {
            using var stream = File.OpenRead(filePath);
            workbook = format == ExcelFormat.Xlsx
                ? new XSSFWorkbook(stream)
                : new HSSFWorkbook(stream);
        }
        catch (Exception ex)
        {
            throw new ExcelReadException(
                "ファイルを開けませんでした。破損、または暗号化（パスワード保護）されている可能性があります。",
                ex);
        }

        try
        {
            var formatter = new DataFormatter();
            var sheets = new List<SheetModel>(workbook.NumberOfSheets);
            for (int i = 0; i < workbook.NumberOfSheets; i++)
            {
                sheets.Add(ReadSheet(workbook.GetSheetAt(i), formatter));
            }

            return new WorkbookModel(filePath, format, sheets);
        }
        finally
        {
            workbook.Close();
        }
    }

    private static SheetModel ReadSheet(ISheet sheet, DataFormatter formatter)
    {
        // 使用範囲（最終行・最終列）を求める。空シートは 0x0。
        int lastRowNum = sheet.LastRowNum; // 0始まり。空シートでも 0 を返すため行存在を別途確認。
        int rowCount = 0;
        int columnCount = 0;

        // まず最終列を確定（全行を走査）。
        for (int r = 0; r <= lastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null)
            {
                continue;
            }

            rowCount = r + 1;
            int rowLastCol = row.LastCellNum; // 最終セル+1（セルが無ければ -1）。
            if (rowLastCol > columnCount)
            {
                columnCount = rowLastCol;
            }
        }

        var cells = new List<IReadOnlyList<CellSource>>(rowCount);
        for (int r = 0; r < rowCount; r++)
        {
            var row = sheet.GetRow(r);
            var rowCells = new CellSource[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                var cell = row?.GetCell(c);
                rowCells[c] = cell == null
                    ? CellSource.Empty
                    : new CellSource(FormatCell(cell, formatter), ExtractFormula(cell));
            }

            cells.Add(rowCells);
        }

        return new SheetModel(sheet.SheetName, cells, rowCount, columnCount);
    }

    /// <summary>数式セルなら "=" 始まりの数式文字列を返す。それ以外は null。</summary>
    private static string? ExtractFormula(ICell cell)
    {
        if (cell.CellType != CellType.Formula)
        {
            return null;
        }

        try
        {
            return "=" + cell.CellFormula;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatCell(ICell cell, DataFormatter formatter)
    {
        // 数式セルはキャッシュされた計算結果を表示する（評価はしない）。
        // 注意: 数式セルに DataFormatter.FormatCellValue(cell) を渡すと数式文字列が返るため、
        // キャッシュ値の型に応じて値を直接取り出す。
        if (cell.CellType == CellType.Formula)
        {
            return cell.CachedFormulaResultType switch
            {
                CellType.Numeric => FormatNumeric(cell, formatter),
                CellType.String => cell.StringCellValue ?? string.Empty,
                CellType.Boolean => cell.BooleanCellValue ? "TRUE" : "FALSE",
                CellType.Error => "#ERROR",
                _ => string.Empty,
            };
        }

        return formatter.FormatCellValue(cell);
    }

    /// <summary>数値（または数式の数値キャッシュ）をセルの表示形式で文字列化する。</summary>
    private static string FormatNumeric(ICell cell, DataFormatter formatter)
    {
        double value = cell.NumericCellValue;
        var style = cell.CellStyle;
        try
        {
            return formatter.FormatRawCellContents(
                value,
                style?.DataFormat ?? 0,
                style?.GetDataFormatString() ?? "General");
        }
        catch
        {
            return value.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }
    }
}
