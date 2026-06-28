namespace ExcelGitDiffViewer.Models;

/// <summary>
/// セル単位の差分種別。
/// </summary>
public enum DiffKind
{
    /// <summary>変更なし（両方空、または同値）。</summary>
    Unchanged,

    /// <summary>値または数式が変更された。背景: 薄黄。</summary>
    Modified,

    /// <summary>右（変更後）のみ値がある／挿入行。背景: 薄緑。</summary>
    AddedRight,

    /// <summary>左（変更前）のみ値がある／削除行。背景: 薄赤。</summary>
    RemovedLeft,

    /// <summary>行アライメントで対向側に対応行が無いギャップ。背景: 薄灰。</summary>
    Gap,
}
