using YuWords.Core;

namespace YuWords;

/// <summary>設定画面（抽出の設定・語の追加分・除外リスト・持ち出し用辞書の保存先）</summary>
sealed class SettingsForm : Form
{
    readonly AppStore store;
    readonly NumericUpDown numDays = new() { Minimum = 1, Maximum = 3650 };
    readonly NumericUpDown numMax = new() { Minimum = 10, Maximum = 100000, Increment = 100 };
    readonly NumericUpDown numMin = new() { Minimum = 1, Maximum = 100 };
    readonly NumericUpDown numLen = new() { Minimum = 2, Maximum = 50 };
    readonly NumericUpDown numRatio = new() { Minimum = 1, Maximum = 100 };
    readonly TextBox txtStop = Multi();
    readonly TextBox txtLead = Multi();
    readonly TextBox txtExcluded = Multi();
    readonly TextBox txtRules = Multi();
    readonly TextBox txtBackup = new() { Width = Dpi.Scale(360) };
    readonly CheckBox chkUseIme = new() { Text = "IME のユーザー辞書を直接使う（直接登録・登録済みの照合。OFF なら辞書ファイルに出力して取り込む）", AutoSize = true, Margin = new Padding(3, 4, 3, 3) };
    readonly CheckBox chkImeDic = new() { Text = "IME のユーザー辞書に既にある語を「登録済み」にする", AutoSize = true, Margin = new Padding(3, 4, 3, 3) };
    readonly CheckBox chkGal = new() { Text = "会社のアドレス帳（Exchange）のふりがなでも補う（時間がかかることがあります）", AutoSize = true, Margin = new Padding(3, 2, 3, 3) };
    readonly NumericUpDown numFolderDays = new() { Minimum = 0, Maximum = 36500 };
    readonly ComboBox cmbTheme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.Scale(150) };
    readonly ComboBox cmbAuto = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.Scale(150) };
    readonly NumericUpDown numAutoHour = new() { Minimum = 0, Maximum = 23 };
    readonly NumericUpDown numFont = new() { Minimum = 8, Maximum = 16, DecimalPlaces = 1, Increment = 0.5m };
    readonly CheckBox chkContacts = new() { Text = "Outlook の連絡先のふりがなで人名のよみを補う", AutoSize = true, Margin = new Padding(3, 10, 3, 3) };

    static TextBox Multi() => new()
    {
        Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Dock = DockStyle.Fill,
    };

    public SettingsForm(AppStore store)
    {
        this.store = store;
        Text = "設定";
        Font = new Font("Yu Gothic UI", 9f);
        Size = new Size(620, 580);
        MinimumSize = new Size(480, 440);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);

        var s = store.Settings;
        numDays.Value = Math.Clamp(s.Extractor.Days, 1, 3650);
        numMax.Value = Math.Clamp(s.Extractor.MaxMails, 10, 100000);
        numMin.Value = Math.Clamp(s.Extractor.MinMails, 1, 100);
        numLen.Value = Math.Clamp(s.Extractor.MaxCompoundLength, 2, 50);
        numRatio.Value = Math.Clamp((int)Math.Round(s.Extractor.CommonLineRatio * 100), 1, 100);
        txtStop.Lines = [.. s.Extractor.ExtraStopWords];
        txtLead.Lines = [.. s.Extractor.ExtraLeadStrip];
        txtExcluded.Lines = [.. store.Excluded];
        txtRules.Lines = [.. s.Extractor.ReadingRules];
        txtBackup.Text = store.BackupFile;
        chkContacts.Text = "Outlook の連絡先のふりがなで人名のよみを補う";
        chkContacts.Checked = s.UseContactReadings;
        chkImeDic.Checked = s.CheckImeDictionary;
        chkUseIme.Checked = s.UseImeDictionary;
        chkGal.Checked = s.UseGalReadings;
        numFolderDays.Value = Math.Clamp(s.FolderDays, 0, 36500);
        numFont.Value = (decimal)Math.Clamp(s.FontSize, 8f, 16f);
        cmbTheme.Items.AddRange(["Windows に合わせる", "明るい", "暗い"]);
        cmbTheme.SelectedIndex = s.ThemeMode switch { "light" => 1, "dark" => 2, _ => 0 };
        cmbAuto.Items.AddRange(["しない", "毎週月曜日", "毎月1日"]);
        cmbAuto.SelectedIndex = (int)s.AutoCheck;
        numAutoHour.Value = Math.Clamp(s.AutoCheckHour, 0, 23);

        tabs.Dock = DockStyle.Fill;
        tabs.TabPages.Add(ExtractPage());
        tabs.TabPages.Add(TextPage("除外リスト", "抽出結果に出さない語（1行に1語）。確認画面の右クリック「除外リストに追加」でも増えます。", txtExcluded));
        tabs.TabPages.Add(WordsPage());
        tabs.TabPages.Add(RulesPage());
        tabs.TabPages.Add(BackupPage());
        tabs.TabPages.Add(RegisteredPage());
        tabs.TabPages.Add(TeamPage());
        tabs.TabPages.Add(UpdatePage());

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6),
        };
        buttons.Controls.AddRange([cancel, ok]);
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, _) => { if (!Save()) DialogResult = DialogResult.None; };

        Controls.Add(tabs);
        Controls.Add(buttons);
    }

    TabPage ExtractPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, Padding = new Padding(10), AutoSize = true };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, NumericUpDown num, string unit)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) });
            num.Width = LogicalToDeviceUnits(90);
            t.Controls.Add(num);
            t.Controls.Add(new Label { Text = unit, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) });
        }
        Row("対象期間", numDays, "日前までの送信メール");
        Row("最大件数", numMax, "件まで読む");
        Row("候補にする出現数", numMin, "通以上に出た語");
        Row("複合語の最大文字数", numLen, "文字");
        Row("署名・定型行とみなす割合", numRatio, "% 以上のメールに出る行");
        Row("フォルダの対象", numFolderDays, "日以内に更新したファイル（0 ならすべて）");
        Row("文字の大きさ", numFont, "ポイント（確認画面）");
        var theme = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
        theme.Controls.AddRange([cmbTheme, new Label { Text = "（次に起動したときから）", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }]);
        t.Controls.Add(new Label { Text = "画面の色", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) });
        t.Controls.Add(theme);
        t.SetColumnSpan(theme, 2);
        var auto = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
        numAutoHour.Width = Dpi.Scale(56);
        auto.Controls.AddRange([cmbAuto, numAutoHour, new Label { Text = "時に新しい候補を調べて通知", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }]);
        t.Controls.Add(new Label { Text = "自動チェック", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) });
        t.Controls.Add(auto);
        t.SetColumnSpan(auto, 2);
        t.Controls.Add(chkContacts);
        t.SetColumnSpan(chkContacts, 3);
        t.Controls.Add(chkGal);
        t.SetColumnSpan(chkGal, 3);
        t.Controls.Add(chkUseIme);
        t.SetColumnSpan(chkUseIme, 3);
        t.Controls.Add(chkImeDic);
        t.SetColumnSpan(chkImeDic, 3);
        var page = new TabPage("抽出");
        page.Controls.Add(t);
        return page;
    }

    TabPage WordsPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        t.Controls.Add(Note("ストップワードの追加分（候補にしない語。1行に1語）"));
        t.Controls.Add(txtStop);
        t.Controls.Add(Note("前置き語の追加分（複合語の先頭から外す語。「以下」「上記」など。1行に1語）"));
        t.Controls.Add(txtLead);
        var page = new TabPage("語の追加分");
        page.Controls.Add(t);
        return page;
    }

    readonly TextBox txtTeam = new() { Width = Dpi.Scale(360) };
    readonly CheckBox chkTeamNotify = new() { Text = "起動したときに、チームの辞書の新しい語を知らせる", AutoSize = true, Margin = new Padding(3, 6, 3, 6) };

    TabPage TeamPage()
    {
        var s = store.Settings;
        txtTeam.Text = s.TeamDictionaryPath;
        chkTeamNotify.Checked = s.TeamNotify;
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Span(Control c) { t.Controls.Add(c); t.SetColumnSpan(c, 2); }
        Span(Note("チームの辞書（社内の共有フォルダに置く CSV）。同僚と用語をそろえるために、みんなで語を足していきます。\n" +
                  "語句・よみ・品詞・追加した人・追加日の列で、Excel でも開けます。無いファイルを選ぶと、最初に足したときに作ります。"));
        txtTeam.Dock = DockStyle.Fill;
        t.Controls.Add(txtTeam);
        var browse = new Button { Text = "参照...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog
            {
                Title = "チームの辞書（CSV）", FileName = txtTeam.Text.Length > 0 ? Path.GetFileName(txtTeam.Text) : "YuWords_チームの辞書.csv",
                Filter = "CSV (*.csv)|*.csv", OverwritePrompt = false, CheckFileExists = false,
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) txtTeam.Text = dlg.FileName;
        };
        t.Controls.Add(browse);
        Span(chkTeamNotify);
        Span(Note("語を足すには、確認画面で語を選んで右クリック →「チームの辞書に足す」。\n" +
                  "新しい語は、起動したときか「ほかから読み込む」→「チームの辞書の新しい語」で一覧に足せます。"));
        return Page("チーム", t);
    }

    readonly CheckBox chkUpdate = new() { Text = "起動したときに新しい版を確かめる（1日に1回まで）", AutoSize = true, Margin = new Padding(3, 6, 3, 6) };
    readonly RadioButton rdoGitHub = new() { Text = "GitHub（インターネット）", AutoSize = true };
    readonly RadioButton rdoFolder = new() { Text = "社内の共有フォルダ（GitHub につながらないとき）", AutoSize = true };
    readonly TextBox txtUpdateFolder = new() { Width = Dpi.Scale(360) };

    TabPage UpdatePage()
    {
        var s = store.Settings;
        chkUpdate.Checked = s.UpdateCheck;
        (s.UpdateSource == "folder" ? rdoFolder : rdoGitHub).Checked = true;
        txtUpdateFolder.Text = s.UpdateFolder;
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        void Span(Control c) { t.Controls.Add(c); t.SetColumnSpan(c, 2); }
        Span(Note($"今の版: v{Updater.CurrentVersion}"));
        Span(chkUpdate);
        Span(Note("新しい版の確認先"));
        Span(rdoGitHub);
        Span(rdoFolder);
        txtUpdateFolder.Dock = DockStyle.Fill;
        t.Controls.Add(txtUpdateFolder);
        var browse = new Button { Text = "参照...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "配布物（YuWords-x.y.z-win-x64.zip）を置く共有フォルダ", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) { txtUpdateFolder.Text = dlg.SelectedPath; rdoFolder.Checked = true; }
        };
        t.Controls.Add(browse);
        Span(Note(
            "GitHub には Windows のプロキシ設定と、ログイン中のアカウントの認証情報で接続します。送るのは版の問い合わせだけです。\n" +
            "社内で配るときは、GitHub のリリースから YuWords-x.y.z-win-x64.zip を共有フォルダに置いてください" +
            "（同じ場所に「zip の名前.sha256」を置くと、壊れていないかも確かめます）。\n" +
            "どちらも使えないときは、「使い方」→「ファイルから更新...」で zip を選んで更新できます。"));
        return Page("更新", t);
    }

    TabPage RulesPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(Note("よみのルール（1行に「表記=よみ」）。複合語の一部に合うときもこのよみを使い、要確認から外します。\n例: 係=がかり、XYZ=みー、設検課=せつけんか。確認画面の右クリック「このよみをルールにする」でも足せます。"));
        t.Controls.Add(txtRules);
        var learned = Note("");
        void ShowLearned() => learned.Text = $"確認画面で直したよみ: {store.Learned.Count} 語（次回の抽出で同じ語に自動で使います）";
        ShowLearned();
        t.Controls.Add(learned);
        var clear = new Button { Text = "直したよみの記録を消す", AutoSize = true };
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "確認画面で直したよみの記録を消します。", "よみのルール", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            store.ClearLearned();
            ShowLearned();
        };
        t.Controls.Add(clear);
        return Page("よみのルール", t);
    }

    TabPage BackupPage()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var note = Note("これまで登録した全語を、IME の取り込み形式で保存するファイルです。OneDrive などに置けば、新しい PC でそのまま取り込めます。");
        t.Controls.Add(note);
        t.SetColumnSpan(note, 2);
        txtBackup.Dock = DockStyle.Fill;
        t.Controls.Add(txtBackup);
        var browse = new Button { Text = "参照...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog
            {
                Title = "持ち出し用辞書の保存先", FileName = Path.GetFileName(txtBackup.Text),
                InitialDirectory = Path.GetDirectoryName(txtBackup.Text), Filter = "テキスト (*.txt)|*.txt",
                OverwritePrompt = false,
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) txtBackup.Text = dlg.FileName;
        };
        t.Controls.Add(browse);
        var info = Note($"設定フォルダ: {store.Dir}");
        t.Controls.Add(info);
        t.SetColumnSpan(info, 2);

        var restoreNote = Note("新しい PC では、前の PC で保存したこのファイル（または IME 取り込み形式のテキスト）を選んで、まとめて IME に登録できます。");
        t.Controls.Add(restoreNote);
        t.SetColumnSpan(restoreNote, 2);
        var restore = new Button { Text = "ファイルを選んで IME に登録...", AutoSize = true };
        restore.Click += async (_, _) => await RestoreFromFile();
        t.Controls.Add(restore);
        return Page("持ち出し用辞書", t);
    }

    /// <summary>登録済みの語を、同僚との共有やほかの IME 用に辞書形式で書き出す（人名も含める）</summary>
    void ExportForSharing()
    {
        if (store.Registered.Count == 0)
        {
            MessageBox.Show(this, "登録済みの語がありません。", "共有用に書き出す");
            return;
        }
        using var dlg = new SaveFileDialog { Title = "共有用に書き出す", FileName = "yuwords_shared.txt", Filter = MainForm.FormatFilter };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var phrases = store.Registered.Count(r => r.Pos == "短縮よみ");
        bool withPhrases = phrases == 0 || MessageBox.Show(this,
            $"定型文（短縮よみ）が {phrases} 語あります。署名など自分だけの定型文も含めますか？\n［いいえ］にすると定型文を除いて書き出します。",
            "共有用に書き出す", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        try
        {
            var path = OutputWriters.WritablePath(dlg.FileName, m => MessageBox.Show(this, m, "共有用に書き出す"));
            var format = (DictFormat)(dlg.FilterIndex - 1);
            int n = DictionaryFormats.Write(path,
                store.Registered.Where(r => withPhrases || r.Pos != "短縮よみ").Select(r => (r.Reading, r.Word, r.Pos)), format);
            MessageBox.Show(this, $"{n} 語を {DictionaryFormats.Label(format)} の形式で書き出しました。\n{path}\n\n" +
                "受け取った人は YuWords の「ほかから読み込む」→「同僚の辞書」で候補として取り込めます。", "共有用に書き出す");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "共有用に書き出す", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void ExportRegisteredCsv()
    {
        using var dlg = new SaveFileDialog { Title = "登録済みの語を CSV に書き出す", FileName = "yuwords_registered.csv", Filter = "CSV (*.csv)|*.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var path = OutputWriters.WritablePath(dlg.FileName, m => MessageBox.Show(this, m, "登録済みの語"));
            OutputWriters.WriteRegisteredCsv(path, store.Registered);
            MessageBox.Show(this, $"{store.Registered.Count} 語を書き出しました。\n{path}", "登録済みの語");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "登録済みの語", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    async Task RestoreFromFile()
    {
        if (await RestoreHelper.FromFileAsync(this, store, txtBackup.Text)) ReloadRegistered();
    }

    /// <summary>IME のユーザー辞書の語を、登録済みの一覧（と持ち出し用辞書）に取り込む</summary>
    async Task ImportFromIme()
    {
        try
        {
            int n = await ImeBusy(() => ImeRegistration.ImportUserDictionary(store));
            ReloadRegistered();
            MessageBox.Show(this, $"IME のユーザー辞書から {n} 語を登録済みの一覧に取り込みました。持ち出し用辞書にも入ります。", "登録済みの語");
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, $"IME のユーザー辞書を開けませんでした。\n{ex.Message}", "登録済みの語", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>IME を使う処理を別のスレッドで行う（画面を固めない）。その間は設定画面を操作できないようにする</summary>
    async Task<T> ImeBusy<T>(Func<T> func)
    {
        UseWaitCursor = true;
        tabs.Enabled = false;
        try { return await StaTask.Run(func); }
        finally
        {
            tabs.Enabled = true;
            UseWaitCursor = false;
        }
    }

    readonly TabControl tabs = new();

    readonly ListView lstRegistered = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
    };
    readonly Label lblRegistered = Note("");
    readonly TextBox txtRegSearch = new() { Width = Dpi.Scale(160), PlaceholderText = "語句・よみで探す" };
    readonly CheckBox chkStale = new() { Text = "使わなくなったかもしれない語だけ", AutoSize = true, Margin = new Padding(10, 5, 3, 3) };
    readonly NumericUpDown numStaleDays = new() { Minimum = 30, Maximum = 3650, Increment = 30, Width = Dpi.Scale(70) };

    TabPage RegisteredPage()
    {
        lstRegistered.Columns.Add("語句", 150);
        lstRegistered.Columns.Add("よみ", 140);
        lstRegistered.Columns.Add("品詞", 70);
        lstRegistered.Columns.Add("登録日", 85);
        lstRegistered.Columns.Add("最後に見た日", 95);
        lstRegistered.Columns.Add("登録先", 90);
        numStaleDays.Value = Math.Clamp(store.Settings.StaleDays, (int)numStaleDays.Minimum, (int)numStaleDays.Maximum);
        ReloadRegistered();

        var edit = new Button { Text = "よみ・品詞を直す...", AutoSize = true };
        var del = new Button { Text = "IME から削除して一覧から外す", AutoSize = true };
        var forget = new Button { Text = "一覧から外すだけ", AutoSize = true };
        var import = new Button { Text = "IME のユーザー辞書から取り込む", AutoSize = true };
        var csvOut = new Button { Text = "CSV に書き出す...", AutoSize = true };
        var csvIn = new Button { Text = "ファイルから登録...", AutoSize = true };
        var share = new Button { Text = "共有用に書き出す...", AutoSize = true };
        share.Click += (_, _) => ExportForSharing();
        new ToolTip().SetToolTip(share, "同僚に渡す・ほかの IME で使うための辞書ファイルを書き出します（Microsoft IME・Google 日本語入力・ATOK）");
        csvOut.Click += (_, _) => ExportRegisteredCsv();
        csvIn.Click += async (_, _) => await RestoreFromFile();
        import.Click += async (_, _) => await ImportFromIme();
        new ToolTip().SetToolTip(import, "ユーザー辞書ツールなど YuWords 以外で登録した語も、持ち出し用辞書に入れます");
        edit.Click += async (_, _) => await EditRegistered();
        lstRegistered.DoubleClick += async (_, _) => await EditRegistered();
        del.Click += async (_, _) => await RemoveRegistered(fromIme: true);
        forget.Click += async (_, _) => await RemoveRegistered(fromIme: false);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange([edit, del, forget, import, csvOut, csvIn, share]);

        txtRegSearch.TextChanged += (_, _) => ReloadRegistered();
        chkStale.CheckedChanged += (_, _) => ReloadRegistered();
        numStaleDays.ValueChanged += (_, _) =>
        {
            store.Settings.StaleDays = (int)numStaleDays.Value;
            if (chkStale.Checked) ReloadRegistered();
        };
        new ToolTip().SetToolTip(chkStale,
            "Outlook の送信メールを読み込むたびに、登録済みの語がメールに出てくるかを調べています。\n" +
            "最後に見かけた日（見かけていなければ登録した日）から、この日数のあいだ一度も出てこなかった語を出します。");
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
        filters.Controls.AddRange([txtRegSearch, chkStale, numStaleDays,
            new Label { Text = "日以上、文章に出てこない", AutoSize = true, Margin = new Padding(3, 6, 3, 3) }]);

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(lblRegistered);
        t.Controls.Add(filters);
        t.Controls.Add(lstRegistered);
        t.Controls.Add(buttons);
        return Page("登録済みの語", t);
    }

    void ReloadRegistered()
    {
        var q = txtRegSearch.Text.Trim();
        var qHira = TermExtractor.KataToHira(q);
        IEnumerable<RegisteredWord> rows = chkStale.Checked
            ? WordUsage.Stale(store.Registered, store.LastUsed, store.Settings.LastUsageScan, (int)numStaleDays.Value)
            : store.Registered;
        if (q.Length > 0)
            rows = rows.Where(r => r.Word.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Reading.Contains(qHira, StringComparison.Ordinal));
        var list = rows.OrderByDescending(r => r.RegisteredAt).ToList();
        lstRegistered.BeginUpdate();
        lstRegistered.Items.Clear();
        foreach (var r in list)
        {
            var seen = store.LastUsed.TryGetValue(r.Word, out var t) ? t.ToString("yyyy/MM/dd") : "";
            lstRegistered.Items.Add(new ListViewItem([r.Word, r.Reading, r.Pos, r.RegisteredAt.ToString("yyyy/MM/dd"), seen,
                r.Target == GoogleIme.Target ? "Google" : "Microsoft IME"]) { Tag = r });
        }
        lstRegistered.EndUpdate();
        var shown = list.Count == store.Registered.Count ? "" : $"（表示 {list.Count} 語）";
        var scan = store.Settings.LastUsageScan is { } s ? $"　文章を最後に調べた日: {s:yyyy/MM/dd}" : "";
        lblRegistered.Text = $"YuWords で登録（出力）した語: {store.Registered.Count} 語{shown}。ここでの操作はすぐに保存されます。{scan}";
        if (chkStale.Checked && store.Settings.LastUsageScan == null)
            lblRegistered.Text += "\nまだ文章を調べていません。Outlook の送信メールを読み込むと、登録済みの語が出てくるかを調べます。";
    }

    List<RegisteredWord> SelectedRegistered() =>
        lstRegistered.SelectedItems.Cast<ListViewItem>().Select(i => (RegisteredWord)i.Tag!).ToList();

    async Task EditRegistered()
    {
        var sel = SelectedRegistered();
        if (sel.Count != 1)
        {
            if (sel.Count > 1) MessageBox.Show(this, "直す語を1つだけ選んでください。", "登録済みの語");
            return;
        }
        var old = sel[0];
        using var ime = new ImeConverter();
        using var f = new AddWordForm(old.Word, w => w.Length == 0 ? "" : ime.Phonetic(w), r => ime.Convert(r), (old.Reading, old.Pos));
        if (f.ShowDialog(this) != DialogResult.OK || (f.Reading == old.Reading && f.Pos == old.Pos)) return;
        var (reading, pos) = (f.Reading, f.Pos);
        try
        {
            bool ok = await ImeBusy(() => ImeRegistration.Edit(old, reading, pos, store));
            if (!ok)
                MessageBox.Show(this, "IME に新しいよみで登録できませんでした。元のまま残しています。", "登録済みの語", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (old.Target == GoogleIme.Target)
                MessageBox.Show(this, "一覧を直しました。Google 日本語入力の辞書は、辞書ツールで直すか、書き出し直して取り込んでください。", "登録済みの語");
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, $"IME に接続できませんでした。一覧は変えていません。\n{ex.Message}", "登録済みの語", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ReloadRegistered();
    }

    async Task RemoveRegistered(bool fromIme)
    {
        var sel = SelectedRegistered();
        if (sel.Count == 0) return;
        var msg = fromIme
            ? $"{sel.Count} 語を IME のユーザー辞書から削除し、登録済みの一覧から外します。"
            : $"{sel.Count} 語を登録済みの一覧から外します（IME のユーザー辞書はそのままです）。次回の抽出で再び候補に出ます。";
        if (MessageBox.Show(this, msg, "登録済みの語", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        try
        {
            if (fromIme)
            {
                int n = await ImeBusy(() => ImeRegistration.Unregister(sel, store));
                int msime = sel.Count(w => w.Target == "MSIME");
                if (n < msime)
                    MessageBox.Show(this, $"IME から削除できたのは {n} 語です（ほかは IME に無かったか、削除できませんでした）。一覧からはすべて外しました。", "登録済みの語");
            }
            else store.RemoveRegistered(sel);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, $"IME に接続できませんでした。一覧は変えていません。\n{ex.Message}\n\nユーザー辞書ツールで削除してから「一覧から外すだけ」を使ってください。",
                "登録済みの語", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ReloadRegistered();
    }

    static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title);
        page.Controls.Add(content);
        return page;
    }

    static TabPage TextPage(string title, string note, TextBox box)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.Controls.Add(Note(note));
        t.Controls.Add(box);
        var page = new TabPage(title);
        page.Controls.Add(t);
        return page;
    }

    static Label Note(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(Dpi.Scale(500), 0), Margin = new Padding(3, 6, 3, 6),
    };

    static List<string> Words(TextBox box) =>
        box.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();

    /// <summary>設定を保存する。持ち出し用辞書の保存先に書けないときは何も変えずに false</summary>
    bool Save()
    {
        var backupText = txtBackup.Text.Trim();
        if (backupText.Length > 0 && !string.Equals(backupText, store.BackupFile, StringComparison.OrdinalIgnoreCase)
            && AppStore.CheckBackupPath(backupText) is { } backupError)
        {
            MessageBox.Show(this, $"持ち出し用辞書の保存先に書き込めません。\n{backupError}\n\n保存先を直してください（設定は保存していません）。",
                "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var s = store.Settings;
        s.Extractor.Days = (int)numDays.Value;
        s.Extractor.MaxMails = (int)numMax.Value;
        s.Extractor.MinMails = (int)numMin.Value;
        s.Extractor.MaxCompoundLength = (int)numLen.Value;
        s.Extractor.CommonLineRatio = (double)numRatio.Value / 100;
        s.Extractor.ExtraStopWords = Words(txtStop);
        s.Extractor.ExtraLeadStrip = Words(txtLead);
        s.Extractor.ReadingRules = txtRules.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
        s.UseContactReadings = chkContacts.Checked;
        s.CheckImeDictionary = chkImeDic.Checked;
        s.UseImeDictionary = chkUseIme.Checked;
        ImeRegistration.Enabled = s.UseImeDictionary;
        s.UseGalReadings = chkGal.Checked;
        s.FolderDays = (int)numFolderDays.Value;
        s.FontSize = (float)numFont.Value;
        s.TeamDictionaryPath = txtTeam.Text.Trim();
        s.TeamNotify = chkTeamNotify.Checked;
        s.UpdateCheck = chkUpdate.Checked;
        s.UpdateSource = rdoFolder.Checked ? "folder" : "github";
        s.UpdateFolder = txtUpdateFolder.Text.Trim();
        s.ThemeMode = cmbTheme.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "auto" };
        var freq = (AutoCheckFrequency)cmbAuto.SelectedIndex;
        var hour = (int)numAutoHour.Value;
        if (freq != s.AutoCheck || hour != s.AutoCheckHour || (freq != AutoCheckFrequency.Off && !AutoCheck.Exists()))
        {
            // タスク スケジューラへの登録・解除は、ここで選んだときだけ行う
            var error = AutoCheck.Apply(Application.ExecutablePath, freq, hour);
            if (error == null)
            {
                (s.AutoCheck, s.AutoCheckHour) = (freq, hour);
                if (freq != AutoCheckFrequency.Off && s.LastOutlookRead == null)
                    MessageBox.Show(this, "自動チェックを登録しました。一度「Outlook から読み込む」を行うと、それ以降の新しいメールを調べるようになります。", "設定");
            }
            else
                MessageBox.Show(this, $"自動チェックを{(freq == AutoCheckFrequency.Off ? "解除" : "登録")}できませんでした。\n{error}", "設定",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        var backup = txtBackup.Text.Trim();
        var defaultBackup = Path.Combine(store.Dir, "dictionary_backup.txt");
        var changed = !string.Equals(backup, store.BackupFile, StringComparison.OrdinalIgnoreCase);
        s.BackupPath = backup.Length == 0 || string.Equals(backup, defaultBackup, StringComparison.OrdinalIgnoreCase) ? "" : backup;
        store.SaveSettings();
        if (changed && store.Registered.Count > 0)
        {
            try { store.WriteBackup(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"持ち出し用辞書を保存できませんでした。\n{ex.Message}", "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        store.Excluded.Clear();
        foreach (var w in Words(txtExcluded)) store.Excluded.Add(w);
        store.SaveExcluded();
        return true;
    }
}
