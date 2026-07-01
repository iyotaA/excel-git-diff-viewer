<#
.SYNOPSIS
  アプリアイコン (app.ico) を複数サイズの PNG から生成する。

.DESCRIPTION
  Assets/icons/ の icon16/32/48/256.png を読み込み、各 PNG のバイト列を
  ICO コンテナへそのまま埋め込んだマルチサイズの app.ico を生成する。
  ImageMagick 等の外部ツールは不要（PowerShell 標準機能のみ）。
  Windows 10/11 は全サイズで PNG-in-ICO を扱えるため再エンコードは行わない。

  出力: src/ExcelGitDiffViewer/Assets/app.ico

.NOTES
  PNG を差し替えた場合はこのスクリプトを再実行すること。
  実行例: pwsh scripts/build-icon.ps1
#>
$ErrorActionPreference = 'Stop'

$root      = Split-Path -Parent $PSScriptRoot
$iconDir   = Join-Path $root 'src\ExcelGitDiffViewer\Assets\icons'
$outPath   = Join-Path $root 'src\ExcelGitDiffViewer\Assets\app.ico'

# 埋め込むアイコンのサイズ（小さい順）
$sizes = 16, 32, 48, 256

$entries = foreach ($s in $sizes) {
    $path = Join-Path $iconDir "icon$s.png"
    if (-not (Test-Path $path)) { throw "PNG が見つかりません: $path" }
    [pscustomobject]@{
        Size  = $s
        Bytes = [System.IO.File]::ReadAllBytes($path)
    }
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
try {
    # --- ICONDIR ヘッダ (6 バイト) ---
    $bw.Write([UInt16]0)                 # reserved (常に 0)
    $bw.Write([UInt16]1)                 # type = 1 (icon)
    $bw.Write([UInt16]$entries.Count)    # 画像数

    # 画像データの開始オフセット = ヘッダ 6 + エントリ 16 * 個数
    $offset = 6 + 16 * $entries.Count

    # --- ICONDIRENTRY (16 バイト × 個数) ---
    foreach ($e in $entries) {
        # 256px は 1 バイトに収まらないため 0 として格納する仕様
        $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
        $bw.Write([Byte]$dim)            # width
        $bw.Write([Byte]$dim)            # height
        $bw.Write([Byte]0)               # color count (パレット未使用 = 0)
        $bw.Write([Byte]0)               # reserved
        $bw.Write([UInt16]1)             # color planes
        $bw.Write([UInt16]32)            # bits per pixel
        $bw.Write([UInt32]$e.Bytes.Length)  # 画像データのバイト数
        $bw.Write([UInt32]$offset)       # 画像データへのオフセット
        $offset += $e.Bytes.Length
    }

    # --- 画像データ本体（PNG バイト列を連結） ---
    foreach ($e in $entries) { $bw.Write($e.Bytes) }

    $bw.Flush()
    [System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
}
finally {
    $bw.Dispose()
    $ms.Dispose()
}

Write-Host "生成しました: $outPath ($([System.IO.FileInfo]::new($outPath).Length) bytes, $($entries.Count) サイズ)"
