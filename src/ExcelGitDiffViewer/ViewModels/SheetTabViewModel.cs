using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ExcelGitDiffViewer.Models;

namespace ExcelGitDiffViewer.ViewModels;

/// <summary>
/// シートタブ1枚分の表示用 ViewModel。タブ見出しと左右の行コレクションを提供する。
/// レビューモード（差分のみ表示）では、表示用コレクションを差分行だけに絞り込む。
/// さらに「追加 / 変更 / 削除」フィルタ（A-5）で差分行の内訳を切り替えられる。
/// </summary>
public sealed class SheetTabViewModel : INotifyPropertyChanged
{
    /// <summary>差分行の種別（A-5 フィルタ用）。</summary>
    private enum RowKind
    {
        Modified,
        Added,   // 右のみ実在（左行の全セルが Gap）
        Removed, // 左のみ実在（右行の全セルが Gap）
    }

    private readonly SheetDiffModel _diff;

    // レビューモード用に差分行だけを事前抽出したコレクション（左右で同数・同じ行揃え）。
    private readonly IReadOnlyList<RowModel> _diffLeftRows;
    private readonly IReadOnlyList<RowModel> _diffRightRows;
    // 各差分行の分類（インデックスは _diffLeftRows / _diffRightRows と対応）。
    private readonly IReadOnlyList<RowKind> _diffRowKinds;

    // フィルタ後の差分行（レビューモード時の表示ソース）。既定は全種類 ON なので _diffLeftRows と一致する。
    private IReadOnlyList<RowModel> _filteredLeftRows;
    private IReadOnlyList<RowModel> _filteredRightRows;

    private bool _isReviewMode;

    public SheetTabViewModel(SheetDiffModel diff)
    {
        _diff = diff;

        // 左右は常に同じ長さ・同じ AlignedIndex で揃っている。どちらかに差分がある
        // インデックスだけを両側から抜き出すことで、左右の行揃えを保ったまま絞り込む。
        var left = new List<RowModel>();
        var right = new List<RowModel>();
        var kinds = new List<RowKind>();
        int count = diff.LeftRows.Count;
        for (int i = 0; i < count; i++)
        {
            var leftRow = diff.LeftRows[i];
            var rightRow = diff.RightRows[i];
            if (RowHasDiff(leftRow) || RowHasDiff(rightRow))
            {
                left.Add(leftRow);
                right.Add(rightRow);
                kinds.Add(ClassifyRow(leftRow, rightRow));
            }
        }

        _diffLeftRows = left;
        _diffRightRows = right;
        _diffRowKinds = kinds;

        // 既定は全種類 ON なのでフィルタ適用しても中身は同じ。
        _filteredLeftRows = _diffLeftRows;
        _filteredRightRows = _diffRightRows;
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

    /// <summary>左 DataGrid にバインドする行（レビューモード時はフィルタ後の差分行、OFF 時は全行）。</summary>
    public IReadOnlyList<RowModel> DisplayLeftRows => _isReviewMode ? _filteredLeftRows : LeftRows;

    /// <summary>右 DataGrid にバインドする行（レビューモード時はフィルタ後の差分行、OFF 時は全行）。</summary>
    public IReadOnlyList<RowModel> DisplayRightRows => _isReviewMode ? _filteredRightRows : RightRows;

    /// <summary>行内のいずれかのセルに差分があるか（ギャップ・挿入・削除行も差分とみなす）。</summary>
    public static bool RowHasDiff(RowModel row) => row.Cells.Any(c => c.Diff != DiffKind.Unchanged);

    /// <summary>
    /// 差分行のフィルタを更新する（A-5）。レビューモード ON 時にのみ表示に反映される。
    /// </summary>
    public void SetFilter(bool showAdded, bool showModified, bool showRemoved)
    {
        // 3つとも ON なら参照を共有して割当を避ける（既定ケース）。
        if (showAdded && showModified && showRemoved)
        {
            if (!ReferenceEquals(_filteredLeftRows, _diffLeftRows))
            {
                _filteredLeftRows = _diffLeftRows;
                _filteredRightRows = _diffRightRows;
                if (_isReviewMode)
                {
                    OnPropertyChanged(nameof(DisplayLeftRows));
                    OnPropertyChanged(nameof(DisplayRightRows));
                }
            }

            return;
        }

        var left = new List<RowModel>();
        var right = new List<RowModel>();
        for (int i = 0; i < _diffRowKinds.Count; i++)
        {
            bool keep = _diffRowKinds[i] switch
            {
                RowKind.Added => showAdded,
                RowKind.Modified => showModified,
                RowKind.Removed => showRemoved,
                _ => true,
            };
            if (keep)
            {
                left.Add(_diffLeftRows[i]);
                right.Add(_diffRightRows[i]);
            }
        }

        _filteredLeftRows = left;
        _filteredRightRows = right;

        if (_isReviewMode)
        {
            OnPropertyChanged(nameof(DisplayLeftRows));
            OnPropertyChanged(nameof(DisplayRightRows));
        }
    }

    /// <summary>1行を「追加 / 削除 / 変更」に分類する。左右の Gap 状態を優先し、それ以外は Modified とみなす。</summary>
    private static RowKind ClassifyRow(RowModel left, RowModel right)
    {
        // 左行のセルがすべて Gap → 右にのみ実在 → 「追加」。
        if (left.Cells.All(c => c.Diff == DiffKind.Gap))
        {
            return RowKind.Added;
        }

        // 右行のセルがすべて Gap → 左にのみ実在 → 「削除」。
        if (right.Cells.All(c => c.Diff == DiffKind.Gap))
        {
            return RowKind.Removed;
        }

        return RowKind.Modified;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
