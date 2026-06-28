<#
.SYNOPSIS
    Excel Git Diff Viewer を git difftool（excel_diff）として登録します（仕様 §5）。

.DESCRIPTION
    ~/.gitconfig（--global）に diff.tool と difftool "excel_diff" のエントリを追加します。
    一度登録すれば `git difftool <file>` で本アプリが起動します。

.PARAMETER ExePath
    ExcelGitDiffViewer.exe のフルパス。省略時はこのスクリプトと同じフォルダの exe を探します。

.PARAMETER Scope
    global（既定）または local（カレントリポジトリのみ）。

.EXAMPLE
    .\install-difftool.ps1
    .\install-difftool.ps1 -ExePath "C:\Program Files\ExcelGitDiffViewer\ExcelGitDiffViewer.exe"
    .\install-difftool.ps1 -Scope local
#>
param(
    [string]$ExePath,
    [ValidateSet('global', 'local')]
    [string]$Scope = 'global'
)

$ErrorActionPreference = 'Stop'

if (-not $ExePath) {
    $candidate = Join-Path $PSScriptRoot 'ExcelGitDiffViewer.exe'
    if (Test-Path $candidate) {
        $ExePath = $candidate
    }
}

if (-not $ExePath -or -not (Test-Path $ExePath)) {
    Write-Error "ExcelGitDiffViewer.exe が見つかりません。-ExePath で明示してください。"
    exit 1
}

# git が使えるか確認。
try {
    git --version | Out-Null
}
catch {
    Write-Error "git コマンドが見つかりません。Git for Windows をインストールしてください。"
    exit 1
}

# git config はスラッシュ区切りのパスを好むため変換。
$exeForGit = (Resolve-Path $ExePath).Path -replace '\\', '/'
$scopeFlag = if ($Scope -eq 'global') { '--global' } else { '--local' }

Write-Host "登録する exe : $exeForGit"
Write-Host "スコープ     : $Scope"

# diff.tool と difftool.excel_diff.cmd を設定。$LOCAL=変更前 / $REMOTE=変更後。
# PowerShell からネイティブ exe へ二重引用符を渡すと失われるため、\" でエスケープして
# git の設定ファイルに引用符付き（パスの空白対応）で保存されるようにする。
$cmdValue = "\`"$exeForGit\`" \`"`$LOCAL\`" \`"`$REMOTE\`""
git config $scopeFlag diff.tool excel_diff
git config $scopeFlag difftool.excel_diff.cmd $cmdValue
git config $scopeFlag difftool.prompt false

Write-Host ""
Write-Host "完了しました。次のように使えます:" -ForegroundColor Green
Write-Host "    git difftool <Excelファイル>"
Write-Host "    git difftool <commitA> <commitB> -- <Excelファイル>"
