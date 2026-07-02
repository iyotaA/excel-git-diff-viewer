namespace ExcelGitDiffViewer.Theme;

/// <summary>
/// テーマ用リソースキーの定数。XAML（Themes/Colors.Dark.xaml, Controls.xaml）の x:Key と一致させ、
/// code-behind / Converter からのキー引き（<see cref="ThemeResources"/>）でマジックストリングを排除する。
/// </summary>
public static class ThemeKeys
{
    // 前景
    public const string FgPrimary = "Fg.Primary";
    public const string FgMuted = "Fg.Muted";

    // 差分セル背景
    public const string DiffChgBg = "Diff.Chg.Bg";
    public const string DiffAddBg = "Diff.Add.Bg";
    public const string DiffDelBg = "Diff.Del.Bg";
    public const string DiffGapBg = "Diff.Gap.Bg";

    // インライン差分（詳細パネル）
    public const string InlineDelBg = "Inline.Del.Bg";
    public const string InlineDelFg = "Inline.Del.Fg";
    public const string InlineInsBg = "Inline.Ins.Bg";
    public const string InlineInsFg = "Inline.Ins.Fg";

    // 数式マーカー "ƒ"
    public const string MarkerFormulaFg = "Marker.Formula.Fg";

    // Style キー（Controls.xaml）
    public const string RowNumberCellStyle = "RowNumberCellStyle";
    public const string RowNumberTextStyle = "RowNumberTextStyle";
}
