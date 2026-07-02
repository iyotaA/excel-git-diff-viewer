using System.Windows;
using System.Windows.Media;

namespace ExcelGitDiffViewer.Theme;

/// <summary>
/// アプリケーションリソースからテーマのブラシをキー引きするヘルパ。
/// XAML の DynamicResource で解決できない箇所（Converter / code-behind の動的生成）から使う。
/// </summary>
public static class ThemeResources
{
    /// <summary>
    /// 指定キーのブラシを取得する。見つからない場合（デザイナ・単体テスト等で
    /// <see cref="Application.Current"/> が null の場合を含む）は <paramref name="fallback"/> を返す。
    /// </summary>
    public static Brush Brush(string key, Brush? fallback = null)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return fallback ?? Brushes.Transparent;
    }
}
