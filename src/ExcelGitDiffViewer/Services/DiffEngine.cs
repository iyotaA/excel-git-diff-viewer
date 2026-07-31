using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExcelGitDiffViewer.Models;

namespace ExcelGitDiffViewer.Services;

/// <summary>
/// 行アライメント方式によるセル差分計算（仕様 §3.4 Phase 2a）。
/// LCS で変更の無い行を対応付け、変更区間内の行は位置で「変更行」としてペアにし、
/// 余った行を挿入（薄緑）/削除（薄赤）として扱う。セル差分は値＋数式で判定する。
/// </summary>
public static class DiffEngine
{
    // 行アライメント（LCS）の計算量上限。これを超える巨大シートは座標比較にフォールバック。
    private const long AlignmentCellBudget = 2_000_000;

    // 行キー組み立て用の区切り（セル値内に通常出現しない制御文字）。
    private const char ValueFormulaSeparator = (char)1;
    private const char CellSeparator = (char)2;

    /// <summary>
    /// 左（変更前）・右（変更後）のワークブックを突合し、シート単位の差分を返す。
    /// </summary>
    public static IReadOnlyList<SheetDiffModel> Compare(WorkbookModel left, WorkbookModel right)
    {
        var result = new List<SheetDiffModel>();
        var rightByName = right.Sheets.ToDictionary(s => s.Name);
        var consumedRight = new HashSet<string>();

        foreach (var leftSheet in left.Sheets)
        {
            if (rightByName.TryGetValue(leftSheet.Name, out var rightSheet))
            {
                consumedRight.Add(leftSheet.Name);
                result.Add(CompareSheet(leftSheet, rightSheet));
            }
            else
            {
                result.Add(BuildSingleSide(leftSheet, isLeft: true));
            }
        }

        foreach (var rightSheet in right.Sheets)
        {
            if (!consumedRight.Contains(rightSheet.Name))
            {
                result.Add(BuildSingleSide(rightSheet, isLeft: false));
            }
        }

        return result;
    }

    private static SheetDiffModel CompareSheet(SheetModel left, SheetModel right)
    {
        int colCount = System.Math.Max(left.ColumnCount, right.ColumnCount);

        var leftKeys = BuildRowKeys(left, colCount);
        var rightKeys = BuildRowKeys(right, colCount);

        var leftRows = new List<RowModel>();
        var rightRows = new List<RowModel>();
        bool anyChange = false;

        var emitter = new RowEmitter(left, right, colCount, leftRows, rightRows);

        if ((long)leftKeys.Length * rightKeys.Length > AlignmentCellBudget)
        {
            // 巨大シート: 行アライメントを諦め座標比較（Phase1 相当）。
            int rows = System.Math.Max(left.RowCount, right.RowCount);
            for (int r = 0; r < rows; r++)
            {
                bool exL = r < left.RowCount;
                bool exR = r < right.RowCount;
                if (exL && exR)
                {
                    anyChange |= emitter.EmitPair(r, r);
                }
                else if (exL)
                {
                    emitter.EmitDeleted(r);
                    anyChange = true;
                }
                else
                {
                    emitter.EmitInserted(r);
                    anyChange = true;
                }
            }
        }
        else
        {
            var anchors = LcsPairs(leftKeys, rightKeys);
            int li = 0, ri = 0;
            foreach (var (al, ar) in anchors)
            {
                anyChange |= EmitBlock(emitter, left, right, li, al, ri, ar);
                emitter.EmitPair(al, ar); // アンカー行（内容一致）
                li = al + 1;
                ri = ar + 1;
            }

            anyChange |= EmitBlock(emitter, left, right, li, left.RowCount, ri, right.RowCount);
        }

        var status = anyChange ? SheetChangeStatus.Modified : SheetChangeStatus.Unchanged;
        return new SheetDiffModel(left.Name, status, leftRows, rightRows, colCount);
    }

    /// <summary>
    /// アンカー間の変更区間 [lStart,lEnd) × [rStart,rEnd) を出力する。変更があれば true。
    /// 先頭列（キー列）で二次 LCS を取り、同じキーを持つ行は「変更行」として対応付ける。
    /// キーが一致しない余りは位置で対応付け、さらに余ったら削除/挿入とする。
    /// </summary>
    private static bool EmitBlock(RowEmitter emitter, SheetModel left, SheetModel right, int lStart, int lEnd, int rStart, int rEnd)
    {
        int lb = lEnd - lStart;
        int rb = rEnd - rStart;
        if (lb <= 0 && rb <= 0)
        {
            return false;
        }

        if (lb <= 0)
        {
            for (int k = 0; k < rb; k++)
            {
                emitter.EmitInserted(rStart + k);
            }

            return true;
        }

        if (rb <= 0)
        {
            for (int k = 0; k < lb; k++)
            {
                emitter.EmitDeleted(lStart + k);
            }

            return true;
        }

        // キー列（先頭列）で二次アライメント。
        var keyL = new string[lb];
        var keyR = new string[rb];
        for (int i = 0; i < lb; i++)
        {
            keyL[i] = KeyColumnOf(left, lStart + i);
        }

        for (int j = 0; j < rb; j++)
        {
            keyR[j] = KeyColumnOf(right, rStart + j);
        }

        var anchors = LcsPairs(keyL, keyR);
        int li = 0, ri = 0;
        foreach (var (al, ar) in anchors)
        {
            PositionalFill(emitter, lStart + li, lStart + al, rStart + ri, rStart + ar);
            emitter.EmitPair(lStart + al, rStart + ar); // 同一キー行 → 変更行として対応付け
            li = al + 1;
            ri = ar + 1;
        }

        PositionalFill(emitter, lStart + li, lEnd, rStart + ri, rEnd);
        return true;
    }

    /// <summary>区間を位置で対応付け、余りを削除/挿入として出力する。</summary>
    private static void PositionalFill(RowEmitter emitter, int lStart, int lEnd, int rStart, int rEnd)
    {
        int lb = lEnd - lStart;
        int rb = rEnd - rStart;
        int paired = System.Math.Min(lb, rb);
        for (int k = 0; k < paired; k++)
        {
            emitter.EmitPair(lStart + k, rStart + k);
        }

        for (int k = paired; k < lb; k++)
        {
            emitter.EmitDeleted(lStart + k);
        }

        for (int k = paired; k < rb; k++)
        {
            emitter.EmitInserted(rStart + k);
        }
    }

    /// <summary>先頭列（キー列）のセル内容。空セルはキーとして弱いため一致扱いを避ける目印を付ける。</summary>
    private static string KeyColumnOf(SheetModel sheet, int row)
    {
        var cell = sheet.CellAt(row, 0);
        if (cell.IsEmpty)
        {
            // 空キー同士を安易に一致させない（行ごとに異なるキーにする）。
            return (char)3 + "k" + row;
        }

        return cell.Value + (char)1 + (cell.Formula ?? string.Empty);
    }

    /// <summary>行内容（値＋数式）を1本の文字列キーにする。</summary>
    private static string[] BuildRowKeys(SheetModel sheet, int colCount)
    {
        var keys = new string[sheet.RowCount];
        var sb = new StringBuilder();
        for (int r = 0; r < sheet.RowCount; r++)
        {
            sb.Clear();
            for (int c = 0; c < colCount; c++)
            {
                var cell = sheet.CellAt(r, c);
                sb.Append(cell.Value)
                  .Append(ValueFormulaSeparator)
                  .Append(cell.Formula ?? string.Empty)
                  .Append(CellSeparator);
            }

            keys[r] = sb.ToString();
        }

        return keys;
    }

    /// <summary>LCS で一致する (leftIndex, rightIndex) のペア列を返す。</summary>
    private static List<(int, int)> LcsPairs(string[] a, string[] b)
    {
        int n = a.Length, m = b.Length;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                dp[i, j] = a[i] == b[j]
                    ? dp[i + 1, j + 1] + 1
                    : System.Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                pairs.Add((x, y));
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }

        return pairs;
    }

    /// <summary>片側のみに存在するシート（追加 / 削除）。</summary>
    private static SheetDiffModel BuildSingleSide(SheetModel sheet, bool isLeft)
    {
        int colCount = sheet.ColumnCount;
        var present = new List<RowModel>(sheet.RowCount);
        var empty = new List<RowModel>(sheet.RowCount);
        var kind = isLeft ? DiffKind.RemovedLeft : DiffKind.AddedRight;

        for (int r = 0; r < sheet.RowCount; r++)
        {
            var presentCells = new CellModel[colCount];
            var gapCells = new CellModel[colCount];
            for (int c = 0; c < colCount; c++)
            {
                var cs = sheet.CellAt(r, c);
                presentCells[c] = new CellModel(r, c, cs.Value, cs.Formula)
                {
                    Diff = cs.IsEmpty ? DiffKind.Unchanged : kind,
                };
                gapCells[c] = new CellModel(r, c, string.Empty) { Diff = DiffKind.Gap };
            }

            int lineCount = MaxLineCount(presentCells);
            present.Add(new RowModel(r, r + 1, isGap: false, presentCells, lineCount));
            empty.Add(new RowModel(r, null, isGap: true, gapCells, lineCount));
        }

        var status = isLeft ? SheetChangeStatus.Removed : SheetChangeStatus.Added;
        return isLeft
            ? new SheetDiffModel(sheet.Name, status, present, empty, colCount)
            : new SheetDiffModel(sheet.Name, status, empty, present, colCount);
    }

    /// <summary>
    /// 行内セルの表示行数の最大値（セル内改行の最大数＋1、最小 1）。
    /// <see cref="RowModel.DisplayLineCount"/> の算出に使う。
    /// </summary>
    private static int MaxLineCount(IReadOnlyList<CellModel> cells)
    {
        int max = 1;
        foreach (var cell in cells)
        {
            int lines = LineCount(cell.Display);
            if (lines > max)
            {
                max = lines;
            }
        }

        return max;
    }

    /// <summary>文字列の行数。"\r\n" は 1 改行として数え、単独の "\r" も改行として扱う。</summary>
    private static int LineCount(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\n')
            {
                lines++;
            }
            else if (ch == '\r')
            {
                // "\r\n" は次の '\n' 側で数えるため、ここでは単独 '\r' のみ数える。
                if (i + 1 >= text.Length || text[i + 1] != '\n')
                {
                    lines++;
                }
            }
        }

        return lines;
    }

    /// <summary>
    /// 左右の RowModel リストへ、揃えた論理行を追加していくヘルパ。
    /// </summary>
    private sealed class RowEmitter
    {
        private readonly SheetModel _left;
        private readonly SheetModel _right;
        private readonly int _colCount;
        private readonly List<RowModel> _leftRows;
        private readonly List<RowModel> _rightRows;

        public RowEmitter(SheetModel left, SheetModel right, int colCount, List<RowModel> leftRows, List<RowModel> rightRows)
        {
            _left = left;
            _right = right;
            _colCount = colCount;
            _leftRows = leftRows;
            _rightRows = rightRows;
        }

        private int NextIndex => _leftRows.Count;

        /// <summary>左右の実行をペアにし、セル差分を付与する。変更があれば true。</summary>
        public bool EmitPair(int leftRowIndex, int rightRowIndex)
        {
            int idx = NextIndex;
            var leftCells = new CellModel[_colCount];
            var rightCells = new CellModel[_colCount];
            bool changed = false;

            for (int c = 0; c < _colCount; c++)
            {
                var lcs = _left.CellAt(leftRowIndex, c);
                var rcs = _right.CellAt(rightRowIndex, c);
                var kind = Classify(lcs, rcs);
                if (kind != DiffKind.Unchanged)
                {
                    changed = true;
                }

                leftCells[c] = new CellModel(leftRowIndex, c, lcs.Value, lcs.Formula) { Diff = kind };
                rightCells[c] = new CellModel(rightRowIndex, c, rcs.Value, rcs.Formula) { Diff = kind };
            }

            int lineCount = System.Math.Max(MaxLineCount(leftCells), MaxLineCount(rightCells));
            _leftRows.Add(new RowModel(idx, leftRowIndex + 1, isGap: false, leftCells, lineCount));
            _rightRows.Add(new RowModel(idx, rightRowIndex + 1, isGap: false, rightCells, lineCount));
            return changed;
        }

        /// <summary>左のみ存在（削除行）。右はギャップ。</summary>
        public void EmitDeleted(int leftRowIndex)
        {
            int idx = NextIndex;
            var leftCells = new CellModel[_colCount];
            var gapCells = new CellModel[_colCount];
            for (int c = 0; c < _colCount; c++)
            {
                var cs = _left.CellAt(leftRowIndex, c);
                leftCells[c] = new CellModel(leftRowIndex, c, cs.Value, cs.Formula)
                {
                    Diff = cs.IsEmpty ? DiffKind.Unchanged : DiffKind.RemovedLeft,
                };
                gapCells[c] = new CellModel(leftRowIndex, c, string.Empty) { Diff = DiffKind.Gap };
            }

            int lineCount = MaxLineCount(leftCells);
            _leftRows.Add(new RowModel(idx, leftRowIndex + 1, isGap: false, leftCells, lineCount));
            _rightRows.Add(new RowModel(idx, null, isGap: true, gapCells, lineCount));
        }

        /// <summary>右のみ存在（挿入行）。左はギャップ。</summary>
        public void EmitInserted(int rightRowIndex)
        {
            int idx = NextIndex;
            var gapCells = new CellModel[_colCount];
            var rightCells = new CellModel[_colCount];
            for (int c = 0; c < _colCount; c++)
            {
                var cs = _right.CellAt(rightRowIndex, c);
                gapCells[c] = new CellModel(rightRowIndex, c, string.Empty) { Diff = DiffKind.Gap };
                rightCells[c] = new CellModel(rightRowIndex, c, cs.Value, cs.Formula)
                {
                    Diff = cs.IsEmpty ? DiffKind.Unchanged : DiffKind.AddedRight,
                };
            }

            int lineCount = MaxLineCount(rightCells);
            _leftRows.Add(new RowModel(idx, null, isGap: true, gapCells, lineCount));
            _rightRows.Add(new RowModel(idx, rightRowIndex + 1, isGap: false, rightCells, lineCount));
        }

        private static DiffKind Classify(in CellSource left, in CellSource right)
        {
            if (left.IsEmpty && right.IsEmpty)
            {
                return DiffKind.Unchanged;
            }

            if (left.IsEmpty)
            {
                return DiffKind.AddedRight;
            }

            if (right.IsEmpty)
            {
                return DiffKind.RemovedLeft;
            }

            return left.ContentEquals(right) ? DiffKind.Unchanged : DiffKind.Modified;
        }
    }
}
