using System.Collections.Generic;

namespace ExcelGitDiffViewer.Models;

/// <summary>VBA モジュール単位の差分状態。</summary>
public enum VbaModuleStatus
{
    Unchanged,
    Modified,
    Added,
    Removed,
}

/// <summary>VBA 差分の1行（左右の行番号・テキスト・差分種別）。</summary>
public sealed class VbaLineRow
{
    public int? LeftNumber { get; }

    public string LeftText { get; }

    public DiffKind LeftKind { get; }

    public int? RightNumber { get; }

    public string RightText { get; }

    public DiffKind RightKind { get; }

    public VbaLineRow(int? leftNumber, string leftText, DiffKind leftKind,
        int? rightNumber, string rightText, DiffKind rightKind)
    {
        LeftNumber = leftNumber;
        LeftText = leftText ?? string.Empty;
        LeftKind = leftKind;
        RightNumber = rightNumber;
        RightText = rightText ?? string.Empty;
        RightKind = rightKind;
    }

    public string LeftNumberDisplay => LeftNumber?.ToString() ?? string.Empty;

    public string RightNumberDisplay => RightNumber?.ToString() ?? string.Empty;
}

/// <summary>1モジュールの差分（名前・状態・行ごとの左右差分）。</summary>
public sealed class VbaModuleDiff
{
    public string Name { get; }

    public VbaModuleStatus Status { get; }

    public IReadOnlyList<VbaLineRow> Lines { get; }

    public VbaModuleDiff(string name, VbaModuleStatus status, IReadOnlyList<VbaLineRow> lines)
    {
        Name = name;
        Status = status;
        Lines = lines;
    }
}
