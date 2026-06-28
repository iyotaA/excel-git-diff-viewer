namespace ExcelGitDiffViewer.Models;

/// <summary>抽出した1つの VBA モジュール（名前とソーステキスト）。</summary>
public sealed class VbaModule
{
    public string Name { get; }

    public string Source { get; }

    public VbaModule(string name, string source)
    {
        Name = name;
        Source = source ?? string.Empty;
    }
}
