# Excel Git Diff Viewer

Excel ファイルの「値」「数式」「VBAマクロ」を抽出し、Git のコミット履歴やワークツリー間の差分を
人間が見やすい形で視覚化・比較する Windows デスクトップアプリ。

> 本リポジトリは **Phase 1〜3 を実装済み**です（MVP / 数式比較・行アライメント・VBA 差分 /
> コミット履歴復元・非同期化・配布自動更新）。
> 詳細仕様は [`docs/アプリケーション仕様書.html`](docs/アプリケーション仕様書.html) を参照してください。

## できること

### データ・数式ビュー
- 引数で受け取った2つの Excel（.xlsx / .xlsm / .xls）の **値・数式** を左右の DataGrid に並べて比較
- 差分セルの背景色ハイライト
  - 🟨 変更　🟩 右のみ（追加）　🟥 左のみ（削除）　⬜ 対応行なし（ギャップ）
- **行アライメント**（LCS）で行・列の挿入/削除を検知（先頭列をキーにした二次対応付け付き）
- 数式セルの **インジケータ（ƒ）** 表示
- セル選択で **下部詳細パネル** に値・数式の差分を表示（DiffPlex によるインライン語句ハイライト）
- 複数シート対応（タブで切替、追加/削除/変更ありを見出しに表示）
- 左右スクロールの同期

### VBA コードビュー（.xlsm）
- `vbaProject.bin` を C# ネイティブ（OpenMcdf ＋ MS-OVBA 展開）で抽出
- モジュール一覧（追加/削除/変更ありを表示）と DiffPlex による行単位の左右差分
- ツールバーの「データ・数式ビュー / VBAコードビュー」で切替

### Git コミット履歴からの比較（Phase 3）
- ツールバーの「Git から開く…」で、リポジトリ管理下の Excel を選択
- コミット履歴（＋ワークツリー）から **変更前 / 変更後の2リビジョン**を選び、その場で比較
- 選択リビジョンのバイナリを一時復元（LibGit2Sharp）

### 共通
- **マジックバイト**による形式判定（拡張子に依存しない）
- `git difftool` 連携の引数受け口（`$LOCAL` `$REMOTE`）
- **非同期読み込み**＋ DataGrid の行・列仮想化で、大きいファイルでも UI がフリーズしない
- 起動時に **GitHub Releases から自動更新チェック**（Velopack、インストール版のみ）

## 必要環境

- Windows 10 / 11
- [.NET 8 SDK](https://aka.ms/dotnet-download)（ビルド時）
  ```powershell
  winget install Microsoft.DotNet.SDK.8
  ```

## ビルドと実行

```powershell
# ビルド
dotnet build -c Release

# 実行（2つの Excel を比較）
dotnet run --project src/ExcelGitDiffViewer -- "before.xlsx" "after.xlsx"

# 引数なしで起動するとファイル選択ダイアログが開きます
dotnet run --project src/ExcelGitDiffViewer
```

ビルド成果物（exe）は `src/ExcelGitDiffViewer/bin/<Config>/net8.0-windows/ExcelGitDiffViewer.exe`。

## git difftool への登録（仕様 §5）

### 同梱スクリプトで自動登録（推奨）

配布物（または `scripts/`）の `install-difftool.ps1` を実行すると `~/.gitconfig` に自動登録されます。

```powershell
# exe と同じフォルダに置かれた install-difftool.ps1 を実行（exe を自動検出）
.\install-difftool.ps1

# exe を明示する場合
.\install-difftool.ps1 -ExePath "C:\Program Files\ExcelGitDiffViewer\ExcelGitDiffViewer.exe"

# カレントリポジトリのみに登録
.\install-difftool.ps1 -Scope local
```

### 手動で登録する場合

`~/.gitconfig` に以下を追記します。

```ini
[diff]
    tool = excel_diff
[difftool "excel_diff"]
    # $LOCAL（比較元）と $REMOTE（比較先）を引数で渡す
    cmd = "C:/Program Files/ExcelGitDiffViewer/ExcelGitDiffViewer.exe" "$LOCAL" "$REMOTE"
[difftool]
    prompt = false
```

使用例:

```bash
# 特定 Excel の最新コミットとの差分
git difftool budget.xlsm

# コミット間の差分
git difftool main feature -- sample.xlsm
```

> difftool が渡す一時ファイルは拡張子を持たない場合がありますが、本アプリは先頭バイト
> （`PK..` = OOXML / `D0 CF 11 E0 …` = OLE2）で形式を判定するため問題なく開けます。

## 配布・自動更新（Velopack / 仕様 §8）

[Velopack](https://velopack.io/) でインストーラ生成・差分更新・リリースノート表示を行います。
更新元は **GitHub Releases**（`src/.../Services/UpdateService.cs` の `GithubRepositoryUrl` を自分の
リポジトリへ変更してください）。

```powershell
# Velopack CLI を導入（初回のみ）
dotnet tool install -g vpk

# 1) アプリを発行
dotnet publish src/ExcelGitDiffViewer -c Release -r win-x64 --self-contained -o publish

# 2) インストーラ＋更新パッケージを生成（バージョンは csproj の <Version> と揃える）
#    --icon で Setup.exe とショートカットのアイコンを指定（アイコンは scripts/build-icon.ps1 で生成）
vpk pack --packId ExcelGitDiffViewer --packVersion 1.0.0 --packDir publish --mainExe ExcelGitDiffViewer.exe --icon src/ExcelGitDiffViewer/Assets/app.ico

# 3) 生成された Releases/ を GitHub Releases へアップロード（vpk upload github も利用可）
```

- `Setup.exe` をダブルクリックでインストール（説明書不要）。
- 起動時にバックグラウンドで更新を確認し、あれば通知 → ワンクリックで差分更新・再起動。
- 開発実行（未インストール）時は更新チェックをスキップします。

### コード署名について（未署名配布の注意 / 仕様 §8）

本構成は **未署名配布**を前提としています。未署名 exe は初回起動時に Windows SmartScreen の
警告が出ることがあります。回避手順をエンドユーザーへ案内してください。

1. 「WindowsによってPCが保護されました」画面で **［詳細情報］** をクリック。
2. **［実行］** ボタンをクリック。

> 警告を出さない構成にするにはコード署名証明書（OV/EV）の導入が必要です。コストと運用に応じて
> 判断してください。証明書を導入する場合は `vpk pack` に署名オプションを付与します。

## プロジェクト構成

```
src/ExcelGitDiffViewer/
  Models/        … データモデル（セル・行・シート・差分 / VBA モジュール差分）
  Services/      … FileFormatDetector / ExcelReader(NPOI) / DiffEngine(行アライメント)
                   InlineTextDiff(DiffPlex) / VbaProjectReader(OpenMcdf+MS-OVBA) / VbaDiffEngine
                   GitService(LibGit2Sharp) / UpdateService(Velopack)
  ViewModels/    … MainViewModel(非同期Load) / SheetTabViewModel / VbaModuleDiffViewModel
  Converters/    … DiffKindToBrushConverter
  MainWindow.*   … データ・数式ビュー / VBAコードビュー / 動的列生成 / スクロール同期 / 詳細パネル
  CommitPickerWindow.* … Git コミット選択ダイアログ
  App.xaml.cs    … 起動・引数処理・Velopack 初期化・更新チェック

scripts/
  install-difftool.ps1 … git difftool への自動登録スクリプト（配布物に同梱）

tools/
  SampleGen/     … 検証用サンプル(before/after.xlsx,.xls)生成
  VbaGen/        … 検証用 VBA 入り .xlsm 生成（CFB ＋ MS-OVBA 圧縮）
  GitTest/       … GitService の headless 検証
```

## ライセンス（使用ライブラリ）

| ライブラリ | ライセンス | 用途 |
|---|---|---|
| NPOI | Apache-2.0 | Excel 値・数式の読み込み |
| DiffPlex | Apache-2.0 | 数式・VBA の行/語句単位差分 |
| OpenMcdf | MPL-2.0 | vbaProject.bin（CFB）の読み込み |
| LibGit2Sharp | MIT | コミット履歴取得・バイナリ復元 |
| Velopack | MIT | インストーラ生成・自動更新 |
