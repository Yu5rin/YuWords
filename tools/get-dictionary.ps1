# UniDic（unidic-lite 1.0.8 に入っている UniDic 2.1.2 の MeCab 用バイナリ）を dict\unidic に取得する。
# 辞書は約 250MB あり Git には入れないため、クローンした後に一度だけ実行する。
# Windows PowerShell 5.1 で動くように書いている（5.1 は BOM 無しの UTF-8 を Shift_JIS として読むため、このファイルは BOM 付き）

[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8

$url    = 'https://files.pythonhosted.org/packages/source/u/unidic-lite/unidic-lite-1.0.8.tar.gz'
$sha256 = 'DB9D4572D9FDD4D00A97949D4B0741EC480EE05A7E7E2E32F547500DAE27B245'
$root   = Split-Path -Parent $PSScriptRoot
$dest   = Join-Path $root 'dict\unidic'
$work   = Join-Path ([IO.Path]::GetTempPath()) ('yuwords-dict-' + [Guid]::NewGuid().ToString('N'))

if (Test-Path (Join-Path $dest 'sys.dic')) {
    Write-Host "辞書はすでにあります: $dest"
    exit 0
}

New-Item -ItemType Directory -Force $work | Out-Null
$archive = Join-Path $work 'unidic-lite.tar.gz'
Write-Host "ダウンロード中（約 47MB）: $url"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
(New-Object Net.WebClient).DownloadFile($url, $archive)

$actual = (Get-FileHash $archive -Algorithm SHA256).Hash
if ($actual -ne $sha256) {
    Write-Host "SHA256 が一致しません（$actual）。中断します。"
    Remove-Item -Recurse -Force $work
    exit 1
}

# Git の tar が先に見つかると C: をホスト名と解釈して失敗するため、Windows 標準の tar.exe を使う
& (Join-Path $env:SystemRoot 'System32\tar.exe') -xzf $archive -C $work
if ($LASTEXITCODE -ne 0) {
    Write-Host '展開に失敗しました。'
    exit 1
}

$src = Join-Path $work 'unidic-lite-1.0.8\unidic_lite\dicdir'
New-Item -ItemType Directory -Force $dest | Out-Null
foreach ($f in 'char.bin', 'dicrc', 'matrix.bin', 'sys.dic', 'unk.dic', 'COPYING', 'BSD', 'README.md', 'version') {
    Copy-Item (Join-Path $src $f) $dest
}
Remove-Item -Recurse -Force $work
Write-Host "完了: $dest"
