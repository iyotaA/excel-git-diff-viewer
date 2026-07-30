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

    // 列フィルタ用。差分を含む列インデックス（昇順）と全列インデックス（0..ColumnCount-1）を事前計算する。
    private readonly IReadOnlyList<int> _diffColumnIndexes;
    private readonly IReadOnlyList<int> _allColumnIndexes;

    private bool _isReviewMode;
    private bool _filterRows = true;
    private bool _filterColumns = true;

    public SheetTabViewModel(SheetDiffModel diff)
    {
        _diff = diff;

        // 左右は常に同じ長さ・同じ AlignedIndex で揃っている。どちらかに差分がある
        // インデックスだけを両側から抜き出すことで、左右の行揃えを保ったまま絞り込む。
        var left = new List<RowModel>();
        var right = new List<RowModel>();
        var kinds = new List<RowKind>();
        int count = diff.LeftRows.Count;
        int colCount = diff.ColumnCount;
        var colHasDiff = new bool[colCount];
        for (int i = 0; i < count; i++)
        {
            var leftRow = diff.LeftRows[i];
            var rightRow = diff.RightRows[i];
            if (RowHasDiff(leftRow) || RowHasDiff(rightRow))
            {
                left.Add(leftRow);
                right.Add(rightRow);
                kinds.Add(ClassifyRow(leftRow, rightRow));

                // 差分列の検出。値が実際に変わった/追加/削除されたセル（Modified / AddedRight / RemovedLeft）を
                // 持つ列だけを差分列とみなす。Gap・Unchanged は除外するので、行追加削除で全列が差分扱いにならない。
                for (int c = 0; c < colCount; c++)
                {
                    if (colHasDiff[c])
                    {
                        continue;
                    }

                    if (IsColumnDiff(leftRow, c) || IsColumnDiff(rightRow, c))
                    {
                        colHasDiff[c] = true;
                    }
                }
            }
        }

        _diffLeftRows = left;
        _diffRightRows = right;
        _diffRowKinds = kinds;

        var diffCols = new List<int>();
        var allCols = new List<int>(colCount);
        for (int c = 0; c < colCount; c++)
        {
            allCols.Add(c);
            if (colHasDiff[c])
            {
                diffCols.Add(c);
            }
        }

        _diffColumnIndexes = diffCols;
        _allColumnIndexes = allCols;

        // 既定は全種類 ON なのでフィルタ適用しても中身は同じ。
        _filteredLeftRows = _diffLeftRows;
        _filteredRightRows = _diffRightRows;
    }

    /// <summary>指定行の指定列セルが差分列の対象（値の変更・追加・削除）か。</summary>
    private static bool IsColumnDiff(RowModel row, int c)
    {
        if (c >= row.Cells.Count)
        {
            return false;
        }

        var d = row.Cells[c].Diff;
        return d == DiffKind.Modified || d == DiffKind.AddedRight || d == DiffKind.RemovedLeft;
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

    /// <summary>差分を含むシートか（レビュー ON 時のタブ絞り込み用。サマリーの ChangedSheetCount と定義を揃える）。</summary>
    public bool HasDiff => DiffRowCount > 0;

    /// <summary>追加行の件数（B-2 サマリー用）。</summary>
    public int AddedRowCount => _diffRowKinds.Count(k => k == RowKind.Added);

    /// <summary>削除行の件数（B-2 サマリー用）。</summary>
    public int RemovedRowCount => _diffRowKinds.Count(k => k == RowKind.Removed);

    /// <summary>変更行の件数（B-2 サマリー用）。</summary>
    public int ModifiedRowCount => _diffRowKinds.Count(k => k == RowKind.Modified);

    /// <summary>
    /// 値または数式が変わったセルの総数（B-2 サマリー用）。
    /// Modified は両側で同じ位置に印が付くため片側（左）でだけ数え、
    /// AddedRight は右側、RemovedLeft は左側でカウントすることで重複カウントを避ける。
    /// </summary>
    public int ChangedCellCount
    {
        get
        {
            int count = 0;
            var leftRows = _diff.LeftRows;
            var rightRows = _diff.RightRows;
            int rowCount = System.Math.Min(leftRows.Count, rightRows.Count);
            for (int r = 0; r < rowCount; r++)
            {
                foreach (var cell in leftRows[r].Cells)
                {
                    if (cell.Diff == DiffKind.Modified || cell.Diff == DiffKind.RemovedLeft)
                    {
                        count++;
                    }
                }
                foreach (var cell in rightRows[r].Cells)
                {
                    if (cell.Diff == DiffKind.AddedRight)
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }

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

    /// <summary>差分のある行だけを表示するか（レビューモードの下位フィルタ、既定 ON）。</summary>
    public bool FilterRows
    {
        get => _filterRows;
        set
        {
            if (_filterRows == value)
            {
                return;
            }

            _filterRows = value;
            if (_isReviewMode)
            {
                OnPropertyChanged(nameof(DisplayLeftRows));
                OnPropertyChanged(nameof(DisplayRightRows));
            }
        }
    }

    /// <summary>差分のある列だけを表示するか（レビューモードの下位フィルタ、既定 ON）。列は code-behind が再生成する。</summary>
    public bool FilterColumns
    {
        get => _filterColumns;
        set => _filterColumns = value;
    }

    /// <summary>
    /// 左 DataGrid にバインドする行。レビューモード ON かつ行フィルタ ON のときだけ差分行に絞る。
    /// </summary>
    public IReadOnlyList<RowModel> DisplayLeftRows => (_isReviewMode && _filterRows) ? _filteredLeftRows : LeftRows;

    /// <summary>
    /// 右 DataGrid にバインドする行。レビューモード ON かつ行フィルタ ON のときだけ差分行に絞る。
    /// </summary>
    public IReadOnlyList<RowModel> DisplayRightRows => (_isReviewMode && _filterRows) ? _filteredRightRows : RightRows;

    /// <summary>
    /// 表示する列インデックス（元シートの列番号）。レビューモード ON かつ列フィルタ ON のときだけ差分列に絞る。
    /// code-behind の列生成が参照する。
    /// </summary>
    public IReadOnlyList<int> DisplayColumnIndexes => (_isReviewMode && _filterColumns) ? _diffColumnIndexes : _allColumnIndexes;

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
