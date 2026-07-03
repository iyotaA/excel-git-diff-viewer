using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ExcelGitDiffViewer.Models;

namespace ExcelGitDiffViewer.Services;

/// <summary>
/// 「最近開いた比較」の JSON 永続化を担う（仕様: 提案書 A-3）。
/// %LOCALAPPDATA%\ExcelGitDiffViewer\recent.json に上限 <see cref="Limit"/> 件で保存する。
/// </summary>
public static class RecentsService
{
    public const int Limit = 10;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    /// <summary>設定ファイルのフルパス。</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExcelGitDiffViewer",
        "recent.json");

    /// <summary>保存済みの履歴を新しい順で読み込む。破損・不在なら空リストで返す。</summary>
    public static List<RecentEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new List<RecentEntry>();
            }

            string json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<RecentEntry>>(json, Options);
            return list ?? new List<RecentEntry>();
        }
        catch
        {
            // 破損時は空で fallback（起動を止めない）。
            return new List<RecentEntry>();
        }
    }

    /// <summary>与えたリストをそのままファイルへ書き出す。</summary>
    public static void Save(IReadOnlyList<RecentEntry> entries)
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(entries, Options);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // 保存失敗は致命的ではない。
        }
    }

    /// <summary>
    /// 新規エントリを先頭へ追加する。同一比較の既存エントリは削除して先頭に移動。
    /// 上限を超えた古いエントリは切り詰める。呼び出し側で <see cref="Save"/> するのが前提。
    /// </summary>
    public static void Add(List<RecentEntry> entries, RecentEntry entry)
    {
        entries.RemoveAll(e => AreSameTarget(e, entry));
        entries.Insert(0, entry);
        if (entries.Count > Limit)
        {
            entries.RemoveRange(Limit, entries.Count - Limit);
        }
    }

    /// <summary>同一の比較対象を指しているか（比較ボタン再クリックで先頭に上がるための同値判定）。</summary>
    private static bool AreSameTarget(RecentEntry a, RecentEntry b)
    {
        if (a.Kind != b.Kind)
        {
            return false;
        }

        return a.Kind switch
        {
            RecentKind.Files =>
                string.Equals(a.LeftPath, b.LeftPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.RightPath, b.RightPath, StringComparison.OrdinalIgnoreCase),
            RecentKind.Git =>
                string.Equals(a.RepoRoot, b.RepoRoot, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase) &&
                a.LeftSha == b.LeftSha && a.RightSha == b.RightSha,
            _ => false,
        };
    }
}
