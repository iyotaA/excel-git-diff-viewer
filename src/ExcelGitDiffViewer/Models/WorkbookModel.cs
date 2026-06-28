using System.Collections.Generic;

namespace ExcelGitDiffViewer.Models;

/// <summary>
/// 1ファイル（ワークブック）の読み込み結果。
/// </summary>
public sealed class WorkbookModel
{
    /// <summary>読み込んだファイルパス。</summary>
    public string FilePath { get; }

    /// <summary>判定された形式。</summary>
    public ExcelFormat Format { get; }

    /// <summary>シート群（ブック内の出現順）。</summary>
    public IReadOnlyList<SheetModel> Sheets { get; }

    public WorkbookModel(string filePath, ExcelFormat format, IReadOnlyList<SheetModel> sheets)
    {
        FilePath = filePath;
        Format = format;
        Sheets = sheets;
    }
}
