using System.Windows;
using ExcelGitDiffViewer.Services;
using ExcelGitDiffViewer.Theme;
using ExcelGitDiffViewer.ViewModels;
using Velopack;

namespace ExcelGitDiffViewer;

/// <summary>
/// アプリ起動処理。git difftool が渡す引数（$LOCAL $REMOTE）を受け取り比較画面を初期化する（仕様 §5）。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Velopack のフック処理（インストール/更新/アンインストール時の引数）を最優先で実行する（仕様 §8）。
        // 通常起動では素通りする。
        VelopackApp.Build().Run();

        base.OnStartup(e);

        // ユーザーが前回選択したテーマを Colors.*.xaml に適用してから UI を組む（B-4）。
        ThemeManager.Initialize();

        var viewModel = new MainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        if (e.Args.Length >= 2)
        {
            // git difftool 連携: 第1引数=変更前($LOCAL) / 第2引数=変更後($REMOTE)。
            _ = viewModel.LoadAsync(e.Args[0], e.Args[1]);
        }

        // 引数なし起動: ホーム画面（比較方法の選択）を表示する。従来のように起動直後に
        // ファイル選択ダイアログを自動で開くことはしない（仕様: 起動時ホーム画面）。

        // 起動時にバックグラウンドで更新チェック（インストール版のみ動作）。
        _ = UpdateService.CheckForUpdatesAsync();
    }
}
