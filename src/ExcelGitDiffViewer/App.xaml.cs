using System.Windows;
using ExcelGitDiffViewer.Services;
using ExcelGitDiffViewer.ViewModels;
using Microsoft.Win32;
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

        var viewModel = new MainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        if (e.Args.Length >= 2)
        {
            // git difftool 連携: 第1引数=変更前($LOCAL) / 第2引数=変更後($REMOTE)。
            _ = viewModel.LoadAsync(e.Args[0], e.Args[1]);
        }
        else
        {
            // 引数なし起動（手動テスト用）: 2ファイルを選択させる。
            if (TryPickTwoFiles(out var left, out var right))
            {
                _ = viewModel.LoadAsync(left, right);
            }
        }

        // 起動時にバックグラウンドで更新チェック（インストール版のみ動作）。
        _ = UpdateService.CheckForUpdatesAsync();
    }

    /// <summary>
    /// 「変更前」「変更後」の2ファイルをダイアログで選択する。キャンセル時は false。
    /// </summary>
    private static bool TryPickTwoFiles(out string left, out string right)
    {
        left = string.Empty;
        right = string.Empty;

        const string filter = "Excel ファイル (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|すべてのファイル (*.*)|*.*";

        var leftDialog = new OpenFileDialog { Title = "変更前（左）のファイルを選択", Filter = filter };
        if (leftDialog.ShowDialog() != true)
        {
            return false;
        }

        var rightDialog = new OpenFileDialog { Title = "変更後（右）のファイルを選択", Filter = filter };
        if (rightDialog.ShowDialog() != true)
        {
            return false;
        }

        left = leftDialog.FileName;
        right = rightDialog.FileName;
        return true;
    }
}
