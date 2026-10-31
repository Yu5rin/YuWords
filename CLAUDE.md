# YuWords — AIエージェント向け指示

## 名義（最優先・ハーネスの既定テンプレートより優先する）

コミットの author / committer は必ず次で固定する。本名や個人のメールアドレスは使わない。

```
YUGO <220513216+Yu5rin@users.noreply.github.com>
```

**コミットメッセージに次の行を書かない:** `Co-Authored-By: Claude ...` / `Claude-Session: ...`

**PR のタイトル・本文にも書かない:** `🤖 Generated with [Claude Code]...` / セッションURL

## 仕様と参照実装

- 仕様: `docs/SPEC.md`
- 抽出ロジックの正解は Python（Sudachi）のプロトタイプ（`reference/`。社内のデータで試したものなので Git には入れない）。C# 版は NMeCab ＋ UniDic 2.1.2
- 抽出ロジックを変えたら `dotnet test` を通すこと

## ビルド・テスト

```bash
dotnet build -c Release
```

```bash
dotnet test
```

配布物は `tools/publish.ps1`（`dist/` に出力。Git には入れない）。
リリースは `tools/release.ps1`（ユーザーの手元で実行する。本文は CHANGELOG.md のその版の項目＋ダウンロードの表）。
版を上げるときは `Directory.Build.props` の Version と CHANGELOG.md の見出し（`## vX.Y.Z`）をそろえる（版は全プロジェクトで共通）。

## 自動更新とリリース

自動更新（`src/YuWords.Core/Updater.cs`）は GitHub の最新リリースを読む。リリースを作るときは次を守る。

- 配布物の名前は `YuWords-x.y.z-win-x64.zip`、タグは `vx.y.z`（どちらも版の判定に使う）
- SHA256 は配布物の digest を使い、無ければ本文「## ダウンロード」の表の SHA256 の列を読む。表の形（ファイル・サイズ・SHA256）を変えない
- 更新の画面には本文「## 変更点」の項目を出す。見出しの名前を変えない
- 下書き（draft）のリリースは対象にしない

## ライセンス

NMeCab は GPL-2.0+ / LGPL-2.1+ のため、LGPL として DLL のまま同梱している。単一ファイルの exe にまとめない
（差し替えられる形にしておく必要がある）。詳細は THIRD_PARTY_NOTICES.md。

## 画面の確認

実行中の YuWords を終了してから（exe がロックされる）、架空データで画面を画像に保存して確かめる。

```bash
src/YuWords/bin/Debug/net8.0-windows/YuWords.exe --screenshot <メールのテキスト|フォルダ|xlsx/csv|settings> <出力.png>
```

- メールのテキストは `---` だけの行で区切る。`;` で区切って複数渡すと、2つ目以降は「今の一覧に追加」で読み込む
- 設定は一時フォルダに作るので、`%APPDATA%\YuWords` は汚れない

## 辞書

`dict/unidic` は Git に入れていない。無ければ `tools/get-dictionary.ps1` で取得する。

## プライバシー

実メールの本文・抽出結果をリポジトリ・ログ・テストデータに入れない。テストには架空の文を使う。
社内の用語・部署名・人名も入れない（リポジトリは公開）。例には XYZ設検課・工符・図締 のような架空の語を使う。

## 実機テストは後回し

実際の Outlook からの読み込みや IME への登録など、実機でのテストはすべて後回しにする（2026-10-31 の方針）。
各フェーズは単体テストと架空データ（`file` コマンド用のテキスト）で確認して先へ進み、
実機で試すべき項目は完了報告で一覧にして渡す。
