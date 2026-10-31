# YuWords 社内で使うときの説明（情報システム部門の方へ）

YuWords を社内で使う・配るときに確かめていただきたいことをまとめました。
機能の説明は README（配布物の `README.md`、または https://github.com/Yu5rin/YuWords ）を見てください。

## 何をするアプリか

利用者が自分で書いた文章（Outlook の送信済みメール、手元のメモ・Word・PowerPoint・Excel）から、
よく使う語（社内用語・人名・略語など）を集め、利用者が確かめた語だけを、その人の Microsoft IME のユーザー辞書に登録します。

- 処理はすべて利用者の PC の中で行います。文章や集めた語を外部に送りません
- 管理者の権限は要りません。インストーラーは無く、zip を展開したフォルダから起動します
- 常駐しません（自動チェックを選んだときだけ、タスク スケジューラから起動して終わります）

## しないこと

- メールの送信・削除・移動、Outlook の設定の変更
- 文章・語・利用状況をインターネットや社外に送ること（送るのは、自動更新の版の問い合わせだけです。下の「通信先」）
- 利用者が選んでいない語を IME に登録すること
- レジストリやシステムの設定を変えること（画面の色を Windows に合わせるため、テーマの設定を読むだけです）

## 通信先

| いつ | 通信先 | 内容 |
|---|---|---|
| 起動したとき（1日に1回まで）と「更新を確認」 | `api.github.com` | 最新の版の問い合わせ（送るのは YuWords の版の番号だけ） |
| 更新するとき | `github.com` と、そこから転送される `*.githubusercontent.com` | 配布物（zip）の取得 |

- Windows のプロキシ設定と、ログイン中のアカウントの認証情報を使います
- 自動更新を止めるとき、または社内の共有フォルダから配るときは、下の「設定をそろえる」で `UpdateCheck` を `false` にするか、`UpdateSource` を `folder` にします。共有フォルダにすると、インターネットには一切つなぎません
- Outlook の連絡先・会社のアドレス帳のふりがなは、Outlook を通して読みます（YuWords から Exchange に直接はつなぎません）

## 使うもの・起動するもの

| もの | 何のため |
|---|---|
| Outlook（クラシック版）の COM | 送信済みメール・連絡先・アドレス帳のふりがなを読む |
| Microsoft IME の COM（IFELanguage・IFEDictionary） | よみ・変換の確認と、ユーザー辞書への登録。辞書を使う処理は、落ちても本体が止まらないよう別のプロセス（`YuWords.exe --ime-worker`）で行う |
| `schtasks.exe` | 自動チェックを選んだときだけ、利用者のタスクとして登録・削除する（タスク名 `YuWords 新しい候補のチェック`） |
| `cmd.exe`・`robocopy.exe`・`tasklist.exe`・`find.exe` | 更新のとき、YuWords を閉じてから新しい版に差し替える |
| Google 日本語入力の辞書ツール | 利用者が「Google 日本語入力に登録」を選んだときだけ |

## データの置き場所

| 場所 | 中身 |
|---|---|
| `%APPDATA%\YuWords\` | 設定・除外リスト・登録した語・直したよみ・途中の一覧（`session.json` はメールの例文を含む）・動作ログ（`logs`。件数・時間・エラーだけで、本文や語は含めない。14日で消す） |
| 持ち出し用辞書（既定は `%APPDATA%\YuWords\dictionary_backup.txt`） | 登録した語の一覧。利用者が OneDrive などに置き場所を変えられる |
| `%LOCALAPPDATA%\YuWords\update\` | 更新のときに取ってきた配布物と、前の版の控え |
| `%TEMP%\YuWords\` | IME の別のプロセスとのやりとり（終わったら消す。残ったものも起動時に消す） |

アンインストールは、展開したフォルダと上の3つのフォルダを消すだけです。自動チェックを使っていたら、先に［設定］で「しない」にしてください（タスクが消えます）。

## 配布物の確かめ方

- 配布物は GitHub のリリース（https://github.com/Yu5rin/YuWords/releases）に置いています
- リリースの本文に、配布物の SHA256 を載せています。手元で確かめるときは PowerShell で次を実行します

```powershell
Get-FileHash YuWords-x.y.z-win-x64.zip -Algorithm SHA256
```

- 自動更新も、取ってきた zip の SHA256 がリリースの値と同じときだけ差し替えます
- 実行ファイルにはコード署名をしていません。SmartScreen の警告が出ることがあります

## 設定をそろえる（社内で配るとき）

YuWords の［設定］→［チーム］→「配布用の既定の設定を書き出す...」で `yuwords.defaults.json` を作り、
配布物の `YuWords.exe` と同じフォルダに置きます。初めて起動した人は、その設定で始まります
（すでに使っている人の設定は変わりません）。

入る項目は次のとおりです（JSON を直接書いても構いません。書いていない項目はふだんの既定のままです）。

| 項目 | 意味 |
|---|---|
| `TeamDictionaryPath` | チームの辞書（共有フォルダの CSV）の場所 |
| `TeamNotify` | 起動したときにチームの辞書の新しい語を知らせるか |
| `UpdateCheck` | 起動したときに新しい版を確かめるか |
| `UpdateSource` | `github` か `folder`（社内の共有フォルダ） |
| `UpdateFolder` | `folder` のときの共有フォルダ（`YuWords-x.y.z-win-x64.zip` を置く。隣に `zip の名前.sha256` を置くと SHA256 も確かめる） |
| `UseContactReadings` / `UseGalReadings` | 連絡先・会社のアドレス帳のふりがなで人名のよみを補うか |
| `UseImeDictionary` / `CheckImeDictionary` | IME のユーザー辞書を直接使うか・照合するか |
| `Extractor` | 抽出の設定（対象期間・最大件数・出現数など） |

例:

```json
{
  "TeamDictionaryPath": "\\\\fileserver\\share\\YuWords\\チームの辞書.csv",
  "UpdateSource": "folder",
  "UpdateFolder": "\\\\fileserver\\share\\YuWords"
}
```

## ライセンス

YuWords は MIT ライセンスです。同梱の NMeCab（形態素解析）は LGPL、UniDic（辞書）は BSD ライセンスです。
詳しくは配布物の `THIRD_PARTY_NOTICES.md` と `licenses` フォルダを見てください。
