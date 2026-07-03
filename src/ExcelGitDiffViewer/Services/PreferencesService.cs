using System;
using System.IO;
using System.Text.Json;

namespace ExcelGitDiffViewer.Services;

/// <summary>アプリの永続化設定（現状はテーマ選択のみ）。JSON ファイルにそのままシリアライズする。</summary>
public sealed class AppPreferences
{
    /// <summary>選択テーマ。"dark" または "light"。未知の値はダーク扱い。</summary>
    public string Theme { get; set; } = "dark";
}

/// <summary>
/// アプリ設定 (preferences.json) の永続化を担う（B-4 ライトテーマ切替の状態保存など）。
/// %LOCALAPPDATA%\ExcelGitDiffViewer\preferences.json に保存し、破損時は既定値で fallback する。
/// </summary>
public static class PreferencesService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    /// <summary>設定ファイルのフルパス。</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExcelGitDiffViewer",
        "preferences.json");

    /// <summary>保存済み設定を読み込む。ファイル不在・破損時は既定値。</summary>
    public static AppPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppPreferences();
            }

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppPreferences>(json, Options) ?? new AppPreferences();
        }
        catch
        {
            return new AppPreferences();
        }
    }

    /// <summary>設定を保存する。書き込み失敗は握りつぶす（アプリの継続動作を優先）。</summary>
    public static void Save(AppPreferences preferences)
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(preferences, Options);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
        }
    }
}
