using System;
using System.Text.Json.Serialization;

namespace ExcelGitDiffViewer.Models;

/// <summary>最近開いた比較エントリの種別。</summary>
public enum RecentKind
{
    /// <summary>2つの Excel ファイルを直接比較。</summary>
    Files,

    /// <summary>Git リポジトリ内の同一ファイルをコミット間で比較。</summary>
    Git,
}

/// <summary>
/// 「最近開いた比較」に保存する1エントリ。ホーム画面の履歴からワンクリック再オープンする。
/// System.Text.Json で直列化するため init-only プロパティを持つ通常クラス（将来のフィールド追加時の後方互換性優先）。
/// </summary>
public sealed class RecentEntry
{
    public RecentKind Kind { get; init; }

    /// <summary>Files のときの左（変更前）ファイル絶対パス。</summary>
    public string? LeftPath { get; init; }

    /// <summary>Files のときの右（変更後）ファイル絶対パス。</summary>
    public string? RightPath { get; init; }

    /// <summary>Git のときのリポジトリのワークツリールート。</summary>
    public string? RepoRoot { get; init; }

    /// <summary>Git のときのリポジトリ相対パス（'/' 区切り）。</summary>
    public string? RelativePath { get; init; }

    /// <summary>Git のときの左 SHA（null はワークツリー）。</summary>
    public string? LeftSha { get; init; }

    /// <summary>Git のときの右 SHA（null はワークツリー）。</summary>
    public string? RightSha { get; init; }

    /// <summary>UI 表示用の左ラベル（ファイル名 or 「file @ shortsha」など）。</summary>
    public string LeftLabel { get; init; } = string.Empty;

    /// <summary>UI 表示用の右ラベル。</summary>
    public string RightLabel { get; init; } = string.Empty;

    /// <summary>比較を実行した時刻（UTC）。並び順・「最新順」表示に使う。</summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>1行タイトル（例: "left.xlsx  ⇄  right.xlsx"）。UI バインディング用。</summary>
    [JsonIgnore]
    public string DisplayTitle => $"{LeftLabel}  ⇄  {RightLabel}";

    /// <summary>種別と時刻を1行にまとめたサブテキスト（UI バインディング用）。</summary>
    [JsonIgnore]
    public string DisplaySubtext
    {
        get
        {
            string icon = Kind switch
            {
                RecentKind.Files => "📂 ファイル比較",
                RecentKind.Git => "🔀 Git 比較",
                _ => string.Empty,
            };
            string when = TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return $"{icon} · {when}";
        }
    }
}
