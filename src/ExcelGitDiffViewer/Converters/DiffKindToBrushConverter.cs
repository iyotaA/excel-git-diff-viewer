using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ExcelGitDiffViewer.Models;

namespace ExcelGitDiffViewer.Converters;

/// <summary>
/// <see cref="DiffKind"/> をセル背景の <see cref="Brush"/> に変換する（仕様 §3.4 / §4 の配色）。
/// </summary>
public sealed class DiffKindToBrushConverter : IValueConverter
{
    // 仕様書のカラートークンに準拠した淡色。
    private static readonly Brush Modified = Freeze(0xFC, 0xF4, 0xD8);   // 薄黄 (--chg-bg)
    private static readonly Brush Added = Freeze(0xE6, 0xF3, 0xEA);     // 薄緑 (--add-bg)
    private static readonly Brush Removed = Freeze(0xFB, 0xEA, 0xEB);   // 薄赤 (--del-bg)
    private static readonly Brush Gap = Freeze(0xF0, 0xF2, 0xF1);       // 薄灰（対応行なし）

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is DiffKind kind
            ? kind switch
            {
                DiffKind.Modified => Modified,
                DiffKind.AddedRight => Added,
                DiffKind.RemovedLeft => Removed,
                DiffKind.Gap => Gap,
                _ => Brushes.Transparent,
            }
            : Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
