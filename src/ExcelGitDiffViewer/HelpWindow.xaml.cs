using System.Reflection;
using System.Windows;
using ExcelGitDiffViewer.Theme;

namespace ExcelGitDiffViewer;

/// <summary>
/// 操作ガイドウィンドウ。ショートカット一覧・基本操作・About を表示する。
/// ツールバーの「?」ボタンおよび F1 キーから開かれる。
/// </summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this, ThemeManager.Current == AppTheme.Dark);
        Loaded += (_, _) => VersionText.Text = GetVersionString();
    }

    /// <summary>アプリのバージョンを AssemblyInformationalVersion → AssemblyVersion の順で取得する。</summary>
    private static string GetVersionString()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            return info;
        }

        return asm.GetName().Version?.ToString(3) ?? "(unknown)";
    }
}
