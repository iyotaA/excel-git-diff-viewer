using System;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace ExcelGitDiffViewer.Services;

/// <summary>
/// Velopack を用いた起動時の自動更新チェック（仕様 §8）。
/// 更新元は GitHub Releases。インストール版でのみ動作し、開発実行時は何もしない。
/// </summary>
public static class UpdateService
{
    // TODO: 配布前に自分のリポジトリURLへ変更すること（例: https://github.com/your-org/ExcelGitDiffViewer）。
    private const string GithubRepositoryUrl = "https://github.com/your-org/ExcelGitDiffViewer";

    /// <summary>
    /// バックグラウンドで更新を確認し、あればユーザーに通知してワンクリック更新・再起動する。
    /// 例外は握りつぶす（更新チェック失敗で本体機能を妨げない）。
    /// </summary>
    public static async Task CheckForUpdatesAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(GithubRepositoryUrl, accessToken: null, prerelease: false));

            // 未インストール（開発実行・ポータブル）時はスキップ。
            if (!mgr.IsInstalled)
            {
                return;
            }

            var updateInfo = await mgr.CheckForUpdatesAsync().ConfigureAwait(true);
            if (updateInfo == null)
            {
                return; // 最新。
            }

            string version = updateInfo.TargetFullRelease.Version.ToString();
            string notes = updateInfo.TargetFullRelease.NotesMarkdown ?? string.Empty;
            string body = string.IsNullOrWhiteSpace(notes)
                ? $"新しいバージョン {version} が利用可能です。今すぐ更新しますか？"
                : $"新しいバージョン {version} が利用可能です。今すぐ更新しますか？\n\n― リリースノート ―\n{notes}";

            var answer = MessageBox.Show(body, "更新があります",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            await mgr.DownloadUpdatesAsync(updateInfo).ConfigureAwait(true);
            mgr.ApplyUpdatesAndRestart(updateInfo);
        }
        catch
        {
            // 更新チェックの失敗は致命的ではない。
        }
    }
}
