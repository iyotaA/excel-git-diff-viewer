using System.Collections.Generic;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace ExcelGitDiffViewer.Services;

/// <summary>インライン差分の1セグメント種別。</summary>
public enum InlineSegmentKind
{
    Unchanged,
    Deleted,
    Inserted,
}

/// <summary>インライン差分の1セグメント（連続する同種テキスト）。</summary>
public readonly record struct InlineSegment(string Text, InlineSegmentKind Kind);

/// <summary>
/// DiffPlex を用いて2つの文字列の語/文字単位インライン差分を計算する（数式詳細パネル用）。
/// </summary>
public static class InlineTextDiff
{
    private static readonly SideBySideDiffBuilder Builder = new(new Differ());

    /// <summary>
    /// 変更前(old)・変更後(new)それぞれのセグメント列を返す。
    /// old 側は不変＋削除、new 側は不変＋挿入で構成される。
    /// </summary>
    public static (IReadOnlyList<InlineSegment> Old, IReadOnlyList<InlineSegment> New) Diff(string? oldText, string? newText)
    {
        var model = Builder.BuildDiffModel(oldText ?? string.Empty, newText ?? string.Empty);
        return (Flatten(model.OldText), Flatten(model.NewText));
    }

    private static List<InlineSegment> Flatten(DiffPaneModel pane)
    {
        var segments = new List<InlineSegment>();
        foreach (var line in pane.Lines)
        {
            if (line.SubPieces is { Count: > 0 })
            {
                foreach (var piece in line.SubPieces)
                {
                    if (piece.Type == ChangeType.Imaginary || piece.Text == null)
                    {
                        continue;
                    }

                    segments.Add(new InlineSegment(piece.Text, Map(piece.Type)));
                }
            }
            else if (line.Type != ChangeType.Imaginary && line.Text != null)
            {
                segments.Add(new InlineSegment(line.Text, Map(line.Type)));
            }
        }

        return segments;
    }

    private static InlineSegmentKind Map(ChangeType type) => type switch
    {
        ChangeType.Deleted => InlineSegmentKind.Deleted,
        ChangeType.Inserted => InlineSegmentKind.Inserted,
        _ => InlineSegmentKind.Unchanged,
    };
}
