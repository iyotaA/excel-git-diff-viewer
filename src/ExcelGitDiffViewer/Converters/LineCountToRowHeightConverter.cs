using System;
using System.Globalization;
using System.Windows.Data;

namespace ExcelGitDiffViewer.Converters;

/// <summary>
/// 行の表示行数（<see cref="Models.RowModel.DisplayLineCount"/>）を DataGridRow の
/// 最小高さ(px)へ変換する。左右ペアは同一の行数を持つため左右の各行ピクセル高が一致し、
/// スクロールのピクセル同期を保ちつつ「改行の多い側」に行高が揃う。
/// <see cref="PerLine"/> はやや上振れさせ、内容が MinHeight を超えて自動成長しないようにして
/// 左右の厳密一致を担保する。極端に多行のセルは <see cref="MaxLines"/> でクランプする
/// （全文は下部の詳細パネルで確認できる）。
/// </summary>
public sealed class LineCountToRowHeightConverter : IValueConverter
{
    /// <summary>1 行あたりの高さ(px)。既定フォント 1 行の実高(≈16-17)に安全側で上振れ。</summary>
    public double PerLine { get; set; } = 17;

    /// <summary>セル Border の上下パディング・罫線などの固定分(px)。1 行時に現行の 24px と一致させる。</summary>
    public double BaseOverhead { get; set; } = 7;

    /// <summary>行高を頭打ちにする最大行数。</summary>
    public int MaxLines { get; set; } = 12;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int lines = value is int n ? n : 1;
        if (lines < 1)
        {
            lines = 1;
        }
        else if (lines > MaxLines)
        {
            lines = MaxLines;
        }

        return BaseOverhead + (lines * PerLine);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
