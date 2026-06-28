using System;
using System.IO;
using ExcelGitDiffViewer;
using ExcelGitDiffViewer.Services;
using LibGit2Sharp;
using NPOI.XSSF.UserModel;

// --ui <pngPath> モード: 実際のコミット履歴で CommitPickerWindow を描画し PNG 保存して終了。
if (args.Length >= 2 && args[0] == "--ui")
{
    int uiResult = 0;
    var staThread = new System.Threading.Thread(() => uiResult = RunUiSmokeTest(args[1]));
    staThread.SetApartmentState(System.Threading.ApartmentState.STA);
    staThread.Start();
    staThread.Join();
    return uiResult;
}

// GitService の headless 検証:
// 一時 git リポジトリを作り、xlsx を2回コミット → 履歴取得 → 各リビジョン復元 → 内容確認。

string root = Path.Combine(Path.GetTempPath(), "egdv_gittest_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Console.WriteLine($"repo: {root}");

try
{
    Repository.Init(root);
    string xlsx = Path.Combine(root, "data.xlsx");
    var sig = new Signature("Tester", "tester@example.com", DateTimeOffset.Now);

    // コミット1: 東京=100
    WriteXlsx(xlsx, "100");
    CommitAll(root, sig, "first");

    // コミット2: 東京=150
    WriteXlsx(xlsx, "150");
    CommitAll(root, sig, "second");

    // ワークツリー: 東京=200（未コミット）
    WriteXlsx(xlsx, "200");

    if (!GitService.TryLocateRepository(xlsx, out var repoRoot, out var rel))
    {
        Console.WriteLine("FAIL: リポジトリを特定できません");
        return 1;
    }

    Console.WriteLine($"located: rel={rel}");
    var history = GitService.GetHistory(repoRoot, rel);
    Console.WriteLine($"history entries: {history.Count} (ワークツリー含む)");
    foreach (var h in history)
    {
        Console.WriteLine($"  - {h.Label}");
    }

    // 先頭=ワークツリー, [1]=second(150), [2]=first(100)
    var workTree = history[0];
    var second = history[1];
    var first = history[2];

    string? wtPath = GitService.RestoreToTemp(repoRoot, rel, workTree);
    string? secondPath = GitService.RestoreToTemp(repoRoot, rel, second);
    string? firstPath = GitService.RestoreToTemp(repoRoot, rel, first);

    string wt = ReadTokyo(wtPath!);
    string s = ReadTokyo(secondPath!);
    string f = ReadTokyo(firstPath!);
    Console.WriteLine($"restored 東京: workTree={wt}, second={s}, first={f}");

    bool ok = wt == "200" && s == "150" && f == "100";
    Console.WriteLine(ok ? "PASS: 全リビジョンの復元に成功" : "FAIL: 値が一致しません");
    return ok ? 0 : 1;
}
finally
{
    try { DeleteRepo(root); } catch { }
}

static void WriteXlsx(string path, string tokyoJan)
{
    var wb = new XSSFWorkbook();
    var s = wb.CreateSheet("売上");
    var r0 = s.CreateRow(0); r0.CreateCell(0).SetCellValue("項目"); r0.CreateCell(1).SetCellValue("1月");
    var r1 = s.CreateRow(1); r1.CreateCell(0).SetCellValue("東京"); r1.CreateCell(1).SetCellValue(tokyoJan);
    using var fs = File.Create(path);
    wb.Write(fs);
    wb.Close();
}

static void CommitAll(string root, Signature sig, string message)
{
    using var repo = new Repository(root);
    Commands.Stage(repo, "*");
    repo.Commit(message, sig, sig);
}

static string ReadTokyo(string path)
{
    var wb = ExcelReader.Read(path);
    return wb.Sheets[0].CellAt(1, 1).Value;
}

static void DeleteRepo(string root)
{
    // .git 配下は読み取り専用属性が付くことがあるため解除してから削除。
    foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
        File.SetAttributes(f, FileAttributes.Normal);
    }

    Directory.Delete(root, recursive: true);
}

// CommitPickerWindow を実際の履歴で描画し PNG 保存する（UI スモークテスト, STA スレッドで実行）。
static int RunUiSmokeTest(string pngPath)
{
    string root = Path.Combine(Path.GetTempPath(), "egdv_uitest_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        Repository.Init(root);
        string xlsx = Path.Combine(root, "予算.xlsx");
        var sig = new Signature("山田太郎", "yamada@example.com", DateTimeOffset.Now);
        WriteXlsx(xlsx, "100");
        CommitAll(root, sig, "初版を追加");
        WriteXlsx(xlsx, "150");
        CommitAll(root, sig, "東京の値を修正");
        WriteXlsx(xlsx, "200"); // ワークツリー

        GitService.TryLocateRepository(xlsx, out var repoRoot, out var rel);
        var history = GitService.GetHistory(repoRoot, rel);

        var app = new System.Windows.Application();
        var picker = new CommitPickerWindow(rel, history);
        picker.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
        picker.Loaded += (_, _) =>
        {
            picker.Dispatcher.BeginInvoke(new Action(() =>
            {
                picker.UpdateLayout();
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)picker.ActualWidth, (int)picker.ActualHeight, 96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(picker);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using (var fs = File.Create(pngPath))
                {
                    enc.Save(fs);
                }

                app.Shutdown();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };
        app.Run(picker);
        Console.WriteLine($"UI shot saved: {pngPath}");
        return 0;
    }
    finally
    {
        try { DeleteRepo(root); } catch { }
    }
}
