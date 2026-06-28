using System.Collections.Generic;
using System.IO;
using LibGit2Sharp;

namespace ExcelGitDiffViewer.Services;

/// <summary>1コミットの表示用情報。Sha が null のときワークツリー（現在の作業ファイル）を表す。</summary>
public sealed record GitCommitInfo(string? Sha, string Label)
{
    public bool IsWorkingTree => Sha == null;

    /// <summary>ワークツリーを表す特別な選択肢。</summary>
    public static readonly GitCommitInfo WorkingTree = new(null, "ワークツリー（現在の作業ファイル）");

    public override string ToString() => Label;
}

/// <summary>
/// Git リポジトリ内の Excel について、コミット履歴の取得と任意コミットのバイナリ復元を行う（仕様 §3.3）。
/// </summary>
public static class GitService
{
    /// <summary>
    /// 指定ファイルを含む Git リポジトリを探す。見つかれば repoRoot と、リポジトリ相対パス（'/' 区切り）を返す。
    /// </summary>
    public static bool TryLocateRepository(string filePath, out string repoRoot, out string relativePath)
    {
        repoRoot = string.Empty;
        relativePath = string.Empty;

        try
        {
            string? full = Path.GetFullPath(filePath);
            string? discovered = Repository.Discover(full);
            if (discovered == null)
            {
                return false;
            }

            using var repo = new Repository(discovered);
            string workdir = repo.Info.WorkingDirectory; // 末尾に区切りを含む
            if (string.IsNullOrEmpty(workdir))
            {
                return false;
            }

            repoRoot = workdir;
            relativePath = Path.GetRelativePath(workdir, full).Replace('\\', '/');
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 指定パスを変更したコミット履歴を新しい順に返す（最大 <paramref name="max"/> 件）。
    /// 先頭に「ワークツリー」を加える。
    /// </summary>
    public static IReadOnlyList<GitCommitInfo> GetHistory(string repoRoot, string relativePath, int max = 100)
    {
        var result = new List<GitCommitInfo> { GitCommitInfo.WorkingTree };

        try
        {
            using var repo = new Repository(repoRoot);
            int count = 0;
            foreach (var logEntry in repo.Commits.QueryBy(relativePath))
            {
                // QueryBy(path) は LogEntry を新しい順に返す。
                result.Add(ToInfo(logEntry.Commit));
                if (++count >= max)
                {
                    break;
                }
            }
        }
        catch
        {
            // 履歴取得に失敗してもワークツリーのみ返す。
        }

        return result;
    }

    private static GitCommitInfo ToInfo(Commit c)
    {
        string shortSha = c.Sha.Length >= 8 ? c.Sha.Substring(0, 8) : c.Sha;
        string when = c.Author.When.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        string summary = c.MessageShort;
        return new GitCommitInfo(c.Sha, $"{shortSha}  {when}  {summary}  ({c.Author.Name})");
    }

    /// <summary>
    /// 指定コミットの該当ファイル内容を一時ファイルへ復元しパスを返す。
    /// ワークツリー指定時は実ファイルパスをそのまま返す。該当無しなら null。
    /// </summary>
    public static string? RestoreToTemp(string repoRoot, string relativePath, GitCommitInfo commit)
    {
        if (commit.IsWorkingTree)
        {
            string actual = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(actual) ? actual : null;
        }

        try
        {
            using var repo = new Repository(repoRoot);
            var c = repo.Lookup<Commit>(commit.Sha);
            var entry = c?[relativePath];
            if (entry?.Target is not Blob blob)
            {
                return null;
            }

            // 拡張子は維持（後続のマジックバイト判定でも問題ないが、デバッグしやすさのため）。
            string ext = Path.GetExtension(relativePath);
            string temp = Path.Combine(Path.GetTempPath(), $"egdv_{commit.Sha![..8]}_{Path.GetFileName(relativePath)}{(string.IsNullOrEmpty(ext) ? ".bin" : string.Empty)}");

            using (var src = blob.GetContentStream())
            using (var dst = File.Create(temp))
            {
                src.CopyTo(dst);
            }

            return temp;
        }
        catch
        {
            return null;
        }
    }
}
