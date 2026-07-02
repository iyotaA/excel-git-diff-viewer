using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ExcelGitDiffViewer.Models;
using ExcelGitDiffViewer.Theme;

namespace ExcelGitDiffViewer.Converters;

/// <summary>
/// <see cref="DiffKind"/> をセル背景の <see cref="Brush"/> に変換する（仕様 §3.4 / §4 の配色）。
/// 配色はテーマリソース（Themes/Colors.Dark.xaml）をキー引きし、配色変更・将来のテーマ切替に追随する。
/// </summary>
public sealed class DiffKindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DiffKind kind)
        {
            return Brushes.Transparent;
        }

        string? key = kind switch
        {
            DiffKind.Modified => ThemeKeys.DiffChgBg,
            DiffKind.AddedRight => ThemeKeys.DiffAddBg,
            DiffKind.RemovedLeft => ThemeKeys.DiffDelBg,
            DiffKind.Gap => ThemeKeys.DiffGapBg,
            _ => null,
        };

        return key == null ? Brushes.Transparent : ThemeResources.Brush(key, FallbackFor(kind));
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// <c>Application.Current</c> が無い場合（デザイナ・単体テスト等）のフォールバック色。
    /// ダーク配色トークンと同値にしておく。
    /// </summary>
    private static Brush FallbackFor(DiffKind kind)
    {
        var (r, g, b) = kind switch
        {
            DiffKind.Modified => (0x3A, 0x33, 0x1A),     // Diff.Chg.Bg
            DiffKind.AddedRight => (0x12, 0x33, 0x1F),   // Diff.Add.Bg
            DiffKind.RemovedLeft => (0x3A, 0x1D, 0x1F),  // Diff.Del.Bg
            DiffKind.Gap => (0x23, 0x23, 0x23),          // Diff.Gap.Bg
            _ => (0, 0, 0),
        };

        var brush = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b));
        brush.Freeze();
        return brush;
    }
}
