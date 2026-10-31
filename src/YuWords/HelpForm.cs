namespace YuWords;

/// <summary>使い方とキー操作の一覧（F1）</summary>
sealed class HelpForm : Form
{
    const string Guide = """
        ■ 流れ
        1. 「Outlook から読み込む」で送信メールから候補を集める（▼で「前回以降の新しいメールだけ」）
           「ほかから読み込む」でフォルダ（Obsidian・テキスト・Word・PowerPoint）や Excel・CSV も読めます
           （Excel・CSV は、用語集の語句とよみの列をそのまま取り込むか、文章の列から語を抽出するかを選べます）
           「今の一覧に追加」を ON にすると、今の一覧に足して数え直します
        2. 一覧で登録する語を選ぶ（読み込み直後は「おすすめ」の高い順）
        3. 「IME に登録」でユーザー辞書に登録（できないときは IME 用テキストに出力して取り込み）
           Google 日本語入力には「Google 日本語入力に登録」で書き出し、開いた辞書ツールの
           ［管理］→［新規辞書にインポート］で取り込みます（ファイルの場所はクリップボードに入ります）

        ■ 色と印
        よみ：黄＝推測（要確認）、赤＝不明（入力が必要）
        IMEのよみ：橙＝推測と食い違う（ダブルクリックで採用）
        語句：青＝同じよみで表記が違う候補がある（表記ゆれ）
        IME変換結果：緑＝そのよみで IME が正しく変換できる
        灰色の行＝登録済み（IME のユーザー辞書にある語も含む）
        確認：✓＝見終えた行、保留＝後回しにした語（次回以降も残る）
        おすすめ：★が多いほど登録する価値が高い（IME で変換できない語・よく使う語）

        ■ キーボードだけで確かめる（上から順に片付ける）
        1            登録する　　　→ 次の未確認の語へ
        2            登録しない　　→ 次の未確認の語へ
        3            保留　　　　　→ 次の未確認の語へ
        4            除外　　　　　→ 次の未確認の語へ
        R            よみを直す（入れたら Enter）

        ■ キー操作
        Enter        見終えた行を確認済みにして次の行へ
        Space        選んだ行の登録 ON/OFF
        Delete       除外リストに追加（次回以降も出さない）
        F2           選んだセルを編集（よみ・語句・別のよみ）
        Ctrl+Z       元に戻す
        Ctrl+N       語を手で追加（クリップボードの語が入ります）
        Ctrl+F       検索欄へ
        Ctrl+K       用語を引く（登録した語とメモ、チームの辞書から探す）
        F1           この画面

        ■ 右クリック
        登録 ON/OFF、IME のよみを採用、短いよみを付ける（品質保証課 → ひほか）、このよみをルールにする、
        品詞を設定、確認済み／未確認、保留／保留を解除、除外リストに追加、元に戻す

        ■ よみを直す
        よみのセルを直すと要確認が外れ、登録チェックが付き、IME の変換結果も取り直します。
        直したよみは覚えておき、次回の抽出で同じ語に使います。
        「このよみをルールにする」と、その語を含む複合語にもそのよみを使います（［設定］→［よみのルール］）。

        ■ 定型文（短縮よみ）
        署名やあいさつなど繰り返し出る行が「定型文」として出ます。短いよみ（例: しょめい）を入れて登録すると、
        そのよみで1行まるごと入力できます。

        ■ チームの辞書
        ［設定］→［チーム］で共有フォルダの CSV を選ぶと、右クリック「チームの辞書に足す」で語を足し、
        起動したときに同僚が足した新しい語を一覧に足せます。

        ■ 登録済みの語の手入れ
        ［設定］→［登録済みの語］で語を探し、よみ・品詞を直したり（ダブルクリック）、
        送信メールに長く出てこない「使わなくなったかもしれない語」を絞り込んで削除したりできます。

        ■ 更新
        起動したときに新しい版を確かめます。「使い方」→「更新を確認...」でも確かめられます。
        会社のネットワークで GitHub につながらないときは、［設定］→［更新］で社内の共有フォルダを確認先にするか、
        「使い方」→「ファイルから更新...」で配布物の zip を選んでください。

        ■ 新しい PC に移るとき
        前の PC で「使い方」→「新しい PC へ引っ越す（まとめて書き出す）...」を選び、
        新しい PC で「使い方」→「前の PC から引っ越す...」でその zip を選ぶと、設定ごと移せます。
        登録した語だけなら、持ち出し用辞書（dictionary_backup.txt）を
        ［設定］→［持ち出し用辞書］→［ファイルを選んで IME に登録...］で選んでください。
        途中の一覧と選択は自動で保存され、次回起動したときに続きから再開します。
        """;

    public HelpForm()
    {
        Text = "YuWords の使い方";
        Font = new Font("Yu Gothic UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);
        Size = new Size(Dpi.Scale(720), Dpi.Scale(640));
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            Text = Guide.ReplaceLineEndings("\r\n"), BackColor = SystemColors.Window, BorderStyle = BorderStyle.None,
        };
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        panel.Controls.Add(box);
        Controls.Add(panel);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Shown += (_, _) => box.Select(0, 0);
    }
}
