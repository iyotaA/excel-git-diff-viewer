using System.Collections.Generic;
using System.Linq;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using ExcelGitDiffViewer.Models;

namespace ExcelGitDiffViewer.Services;

/// <summary>
/// DiffPlex を用いて VBA モジュールの行単位差分を計算する（仕様 §3.2 / §3.4 VBA タブ）。
/// </summary>
public static class VbaDiffEngine
{
    private static readonly SideBySideDiffBuilder Builder = new(new Differ());

    /// <summary>
    /// 変更前・変更後のモジュール一覧を名前で突合し、モジュール単位の差分を返す。
    /// </summary>
    public static IReadOnlyList<VbaModuleDiff> Compare(
        IReadOnlyList<VbaModule> before, IReadOnlyList<VbaModule> after)
    {
        var result = new List<VbaModuleDiff>();
        var afterByName = after.ToDictionary(m => m.Name, System.StringComparer.OrdinalIgnoreCase);
        var consumed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var b in before)
        {
            if (afterByName.TryGetValue(b.Name, out var a))
            {
                consumed.Add(b.Name);
                result.Add(BuildDiff(b.Name, b.Source, a.Source));
            }
            else
            {
                result.Add(BuildDiff(b.Name, b.Source, string.Empty));
            }
        }

        foreach (var a in after)
        {
            if (!consumed.Contains(a.Name))
            {
                result.Add(BuildDiff(a.Name, string.Empty, a.Source));
            }
        }

        result.Sort((x, y) => string.Compare(x.Name, y.Name, System.StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static VbaModuleDiff BuildDiff(string name, string beforeSource, string afterSource)
    {
        var model = Builder.BuildDiffModel(beforeSource, afterSource);
        var rows = new List<VbaLineRow>(System.Math.Max(model.OldText.Lines.Count, model.NewText.Lines.Count));
        bool anyChange = false;

        int count = System.Math.Max(model.OldText.Lines.Count, model.NewText.Lines.Count);
        for (int i = 0; i < count; i++)
        {
            var left = i < model.OldText.Lines.Count ? model.OldText.Lines[i] : null;
            var right = i < model.NewText.Lines.Count ? model.NewText.Lines[i] : null;

            var leftKind = Map(left?.Type ?? ChangeType.Imaginary);
            var rightKind = Map(right?.Type ?? ChangeType.Imaginary);
            if (leftKind != DiffKind.Unchanged || rightKind != DiffKind.Unchanged)
            {
                anyChange = true;
            }

            rows.Add(new VbaLineRow(
                left?.Position, left?.Text ?? string.Empty, leftKind,
                right?.Position, right?.Text ?? string.Empty, rightKind));
        }

        VbaModuleStatus status;
        if (string.IsNullOrEmpty(beforeSource))
        {
            status = VbaModuleStatus.Added;
        }
        else if (string.IsNullOrEmpty(afterSource))
        {
            status = VbaModuleStatus.Removed;
        }
        else
        {
            status = anyChange ? VbaModuleStatus.Modified : VbaModuleStatus.Unchanged;
        }

        return new VbaModuleDiff(name, status, rows);
    }

    private static DiffKind Map(ChangeType type) => type switch
    {
        ChangeType.Deleted => DiffKind.RemovedLeft,
        ChangeType.Inserted => DiffKind.AddedRight,
        ChangeType.Modified => DiffKind.Modified,
        ChangeType.Imaginary => DiffKind.Gap,
        _ => DiffKind.Unchanged,
    };
}
