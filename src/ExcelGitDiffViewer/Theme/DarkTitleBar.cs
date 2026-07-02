using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ExcelGitDiffViewer.Theme;

/// <summary>
/// Windows のタイトルバー（ノンクライアント領域）をダークにする DWM ヘルパ。
/// Windows 10 バージョン2004 以降で有効。未対応 OS では単に無視される。
/// </summary>
public static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// 指定ウィンドウのタイトルバーをダークにする。ハンドルが必要なため
    /// <see cref="Window.SourceInitialized"/> のタイミングで呼ぶこと。
    /// </summary>
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int useDark = 1;
        // まず属性値 20（20H1 以降）で試し、古いビルド向けに 19 でも試す。
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDark, sizeof(int));
        }
    }
}
