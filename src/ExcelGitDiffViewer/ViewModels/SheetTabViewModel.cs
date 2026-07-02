using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ExcelGitDiffViewer.Models;

namespace ExcelGitDiffViewer.ViewModels;

/// <summary>
/// シートタブ1枚分の表示用 ViewModel。タブ見出しと左右の行コレクションを提供する。
/// レビューモード（差分のみ表示）では、表示用コレクションを差分行だけに絞り込む。
/// </summary>
public sealed class SheetTabViewModel : INotifyPropertyChanged
{
    private readonly SheetDiffModel _diff;

    // レビューモード用に差分行だけを事前抽出したコレクション（左右で同数・同じ行揃え）。
    private readonly IReadOnlyList<RowModel> _diffLeftRows;
    private readonly IReadOnlyList<RowModel> _diffRightRows;

    private bool _isReviewMode;

    public SheetTabViewModel(SheetDiffModel diff)
    {
        _diff = diff;

        // 左右は常に同じ長さ・同じ AlignedIndex で揃っている。どちらかに差分がある
        // インデックスだけを両側から抜き出すことで、左右の行揃えを保ったまま絞り込む。
        var left = new List<RowModel>();
        var right = new List<RowModel>();
        int count = diff.LeftRows.Count;
        for (int i = 0; i < count; i++)
        {
            if (RowHasDiff(diff.LeftRows[i]) || RowHasDiff(diff.RightRows[i]))
            {
                left.Add(diff.LeftRows[i]);
                right.Add(diff.RightRows[i]);
            }
        }

        _diffLeftRows = left;
        _diffRightRows = right;
    }

    public string Name => _diff.Name;

    /// <summary>タブ見出し（状態マーカー付き, 仕様 §3.5 / §4）。</summary>
    public string Header => _diff.Status switch
    {
        SheetChangeStatus.Added => $"{_diff.Name}（追加）",
        SheetChangeStatus.Removed => $"{_diff.Name}（削除）",
        SheetChangeStatus.Modified => $"{_diff.Name}（変更あり）",
        _ => _diff.Name,
    };

    public IReadOnlyList<RowModel> LeftRows => _diff.LeftRows;

    public IReadOnlyList<RowModel> RightRows => _diff.RightRows;

    public int ColumnCount => _diff.ColumnCount;

    /// <summary>差分を含む行数（レビューモードの ON/OFF に依らない固定値）。</summary>
    public int DiffRowCount => _diffLeftRows.Count;

    /// <summary>レビューモード（差分のみ表示）か。切替時に表示用コレクションを差し替える。</summary>
    public bool IsReviewMode
    {
        get => _isReviewMode;
        set
        {
            if (_isReviewMode == value)
            {
                return;
            }

            _isReviewMode = value;
            OnPropertyChanged(nameof(DisplayLeftRows));
            OnPropertyChanged(nameof(DisplayRightRows));
        }
    }

    /// <summary>左 DataGrid にバインドする行（レビューモード時は差分行のみ）。</summary>
    public IReadOnlyList<RowModel> DisplayLeftRows => _isReviewMode ? _diffLeftRows : LeftRows;

    /// <summary>右 DataGrid にバインドする行（レビューモード時は差分行のみ）。</summary>
    public IReadOnlyList<RowModel> DisplayRightRows => _isReviewMode ? _diffRightRows : RightRows;

    /// <summary>行内のいずれかのセルに差分があるか（ギャップ・挿入・削除行も差分とみなす）。</summary>
    public static bool RowHasDiff(RowModel row) => row.Cells.Any(c => c.Diff != DiffKind.Unchanged);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
