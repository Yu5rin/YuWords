# GitHub のリリースを作る（配布物の zip を付ける）。手元の PC で実行する。
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1            … 下書きとして作る（確かめてから公開）
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Publish   … すぐ公開する
#   -SkipPublish を付けると、dist にある zip をそのまま使う（作り直さない）
#
# リリースのタイトルはタグと同じ（v1.2.3）。本文は「## 変更点」（CHANGELOG.md のその版の項目）と
# 「## ダウンロード」（ファイル・サイズ・SHA256 の表）、最後に README への案内1行だけにする。
# Windows PowerShell 5.1 で動くように書いている（このファイルは BOM 付き UTF-8）。
# 5.1 は外部コマンドが標準エラーに書いただけで止まることがあるので、$ErrorActionPreference は変えず、成否は $LASTEXITCODE で見る。

param([switch]$Publish, [switch]$SkipPublish)

[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = (Select-Xml -Path (Join-Path $root 'Directory.Build.props') -XPath '//Version').Node.InnerText
if (-not $version) { Write-Host 'Directory.Build.props に Version がありません。'; exit 1 }
$tag = "v$version"

gh --version | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'GitHub CLI（gh）が見つかりません。'; exit 1 }
gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'gh にログインしていません。gh auth login を実行してください。'; exit 1 }

$dirty = git status --porcelain
if ($dirty) { Write-Host 'コミットしていない変更があります。先にコミットしてください。'; exit 1 }
git fetch --quiet origin 2>&1 | Out-Null
$ahead = git rev-list --count '@{u}..HEAD' 2>$null
if ($LASTEXITCODE -ne 0) { Write-Host '今のブランチが GitHub のブランチと結び付いていません。先に push してください。'; exit 1 }
if ([int]$ahead -gt 0) { Write-Host "push していないコミットが $ahead 個あります。先に push してください。"; exit 1 }

gh release view $tag 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) { Write-Host "リリース $tag はすでにあります。版を上げてから実行してください。"; exit 1 }

# CHANGELOG.md から、その版の項目を取り出す
$changelog = [IO.File]::ReadAllLines((Join-Path $root 'CHANGELOG.md'), [Text.Encoding]::UTF8)
$items = @()
$inSection = $false
foreach ($line in $changelog) {
    if ($line -match '^## ') {
        if ($inSection) { break }
        $inSection = ($line.Trim() -eq "## $tag")
        continue
    }
    if ($inSection -and $line.Trim().Length -gt 0) { $items += $line }
}
if ($items.Count -eq 0) { Write-Host "CHANGELOG.md に「## $tag」の項目がありません。"; exit 1 }

if (-not $SkipPublish) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\publish.ps1')
    if ($LASTEXITCODE -ne 0) { Write-Host '配布物を作れませんでした。'; exit 1 }
}
$zip = Join-Path $root "dist\YuWords-$version-win-x64.zip"
if (-not (Test-Path $zip)) { Write-Host "配布物がありません: $zip"; exit 1 }

$name = Split-Path -Leaf $zip
$size = '{0:N1} MB' -f ((Get-Item $zip).Length / 1MB)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
$repoUrl = gh repo view --json url -q .url
if ($LASTEXITCODE -ne 0) { Write-Host 'GitHub のリポジトリが分かりません。'; exit 1 }

$body = @()
$body += '## 変更点'
$body += ''
$body += $items
$body += ''
$body += '## ダウンロード'
$body += ''
$body += '| ファイル | サイズ | SHA256 |'
$body += '|---|---|---|'
$body += "| $name | $size | ``$hash`` |"
$body += ''
$body += "インストール方法・使い方は [README]($repoUrl#readme) を見てください。"

$notes = Join-Path ([IO.Path]::GetTempPath()) "yuwords-release-$tag.md"
[IO.File]::WriteAllText($notes, ($body -join "`n"), (New-Object Text.UTF8Encoding($false)))

$ghArgs = @('release', 'create', $tag, $zip, '--title', $tag, '--notes-file', $notes, '--target', (git rev-parse HEAD))
if (-not $Publish) { $ghArgs += '--draft' }
gh @ghArgs
if ($LASTEXITCODE -ne 0) { Write-Host 'リリースを作れませんでした。'; exit 1 }
Remove-Item $notes
if ($Publish) { Write-Host "公開しました: $tag" } else { Write-Host "下書きを作りました: $tag（GitHub で内容を確かめてから公開してください）" }
