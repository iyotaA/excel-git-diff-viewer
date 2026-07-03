using System;
using System.Windows;
using ExcelGitDiffViewer.Services;

namespace ExcelGitDiffViewer.Theme;

/// <summary>アプリの外観テーマ（B-4）。</summary>
public enum AppTheme
{
    Dark,
    Light,
}

/// <summary>
/// テーマ差替の唯一の入口（B-4）。<see cref="Application.Resources"/> の
/// MergedDictionaries[0] を Colors.Dark.xaml / Colors.Light.xaml と差し替える。
/// 起動時に <see cref="Initialize"/> を呼び、ユーザー操作からは <see cref="Toggle"/> を呼ぶ。
/// </summary>
public static class ThemeManager
{
    private const string DarkSource = "/Themes/Colors.Dark.xaml";
    private const string LightSource = "/Themes/Colors.Light.xaml";

    /// <summary>現在適用中のテーマ。</summary>
    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    /// <summary>アプリ起動時に保存済みテーマを読み込んで適用する（<see cref="App.OnStartup"/> から呼ぶ）。</summary>
    public static void Initialize()
    {
        var prefs = PreferencesService.Load();
        var theme = ParseTheme(prefs.Theme);
        Apply(theme);
    }

    /// <summary>指定テーマを適用する。既に同一テーマなら何もしない。</summary>
    public static void Apply(AppTheme theme)
    {
        var app = Application.Current;
        if (app == null)
        {
            Current = theme;
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;
        if (dictionaries.Count == 0)
        {
            Current = theme;
            return;
        }

        var newDict = new ResourceDictionary
        {
            Source = new Uri(theme == AppTheme.Dark ? DarkSource : LightSource, UriKind.Relative),
        };

        // 色定義は先頭 (Controls.xaml が参照するため)。先頭を差し替える。
        dictionaries[0] = newDict;
        Current = theme;

        RefreshTitleBars();
    }

    /// <summary>テーマを反転し、保存する。</summary>
    public static AppTheme Toggle()
    {
        var next = Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        Apply(next);
        PreferencesService.Save(new AppPreferences { Theme = next == AppTheme.Dark ? "dark" : "light" });
        return next;
    }

    /// <summary>開いているすべてのウィンドウのタイトルバー配色を現在のテーマに揃える。</summary>
    public static void RefreshTitleBars()
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        bool useDark = Current == AppTheme.Dark;
        foreach (Window window in app.Windows)
        {
            DarkTitleBar.Apply(window, useDark);
        }
    }

    private static AppTheme ParseTheme(string? value)
    {
        return string.Equals(value, "light", StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Light
            : AppTheme.Dark;
    }
}
