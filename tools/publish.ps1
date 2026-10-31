# 配布用のポータブル版を dist\YuWords に作り、zip にまとめる。
# .NET ランタイムを同梱する（会社の PC で .NET を入れられなくても動くように）。インストーラは使わない。
# Windows PowerShell 5.1 で動くように書いている（このファイルは BOM 付き UTF-8）

[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8

$root = Split-Path -Parent $PSScriptRoot
$out  = Join-Path $root 'dist\YuWords'

if (-not (Test-Path (Join-Path $root 'dict\unidic\sys.dic'))) {
    Write-Host '辞書がありません。先に tools\get-dictionary.ps1 を実行してください。'
    exit 1
}

if (Test-Path $out) { Remove-Item -Recurse -Force $out }

foreach ($proj in 'src\YuWords\YuWords.csproj', 'src\YuWords.Cli\YuWords.Cli.csproj') {
    Write-Host "発行中: $proj"
    dotnet publish (Join-Path $root $proj) -c Release -r win-x64 --self-contained true -o $out -v q --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "発行に失敗しました: $proj"
        exit 1
    }
}

# 開発用のファイルは配らない
Get-ChildItem $out -Filter *.pdb | Remove-Item

# 同梱しているソフトウェアのライセンス表記と、使い方
Copy-Item (Join-Path $root 'THIRD_PARTY_NOTICES.md') $out
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $out 'LICENSE.txt')
Copy-Item (Join-Path $root 'README.md') $out
Copy-Item (Join-Path $root 'licenses') (Join-Path $out 'licenses') -Recurse

$version = (Select-Xml -Path (Join-Path $root 'src\YuWords\YuWords.csproj') -XPath '//Version').Node.InnerText
if (-not $version) { $version = 'dev' }
$zip = Join-Path $root "dist\YuWords-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Write-Host "zip にまとめています: $zip"
Compress-Archive -Path $out -DestinationPath $zip

$size = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "完了: $zip（$size MB）"
Write-Host "SHA256: $hash"
