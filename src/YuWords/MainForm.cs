using System.Diagnostics;
using YuWords.Core;

namespace YuWords;

/// <summary>確認画面（メイン画面）。候補一覧の取捨選択・よみ修正・出力を行う</summary>
sealed partial class MainForm : Form
{
    enum Col { Register, Check, Stars, Word, Reading, ImeReading, Extra, Pos, MailCount, Category, Signature, Ime, Unsure, Example }

    readonly AppStore store;
    List<ReviewItem> all = [];
    List<ReviewItem> view = [];
    readonly ReviewFilter filter = new();
    Col? sortCol;
    bool sortDesc;
    CancellationTokenSource? cts;

    readonly DataGridView grid = new();
    readonly ToolStripSplitButton btnOutlook = new("Outlook から読み込む");
    readonly ToolStripMenuItem mnuOutlookNew = new("前回以降の新しいメールだけ読み込む");
    readonly CheckBox chkUnreviewed = new() { Text = "未確認のみ", AutoSize = true };
    readonly CheckBox chkHeld = new() { Text = "保留中も表示", AutoSize = true };
    readonly ToolStripDropDownButton btnOther = new("ほかから読み込む");
    readonly ToolStripButton btnCancel = new("中止") { Enabled = false };
    readonly ToolStripButton btnAppend = new("今の一覧に追加") { CheckOnClick = true, ToolTipText = "ON にすると、次に読み込む内容を今の一覧に足します（選択は残り、出現数は合算）" };
    readonly ToolStripButton btnSettings = new("設定...");
    readonly ToolStripButton btnDictTool = new("ユーザー辞書ツール");
    readonly TextBox txtSearch = new() { Width = Dpi.Scale(160), PlaceholderText = "語句・よみで検索" };
    readonly CheckBox chkUnsure = new() { Text = "要確認のみ", AutoSize = true };
    readonly CheckBox chkSig = new() { Text = "署名由来のみ", AutoSize = true };
    readonly CheckBox chkNotConv = new() { Text = "IMEで変換不可のみ", AutoSize = true };
    readonly CheckBox chkChecked = new() { Text = "登録チェック済みのみ", AutoSize = true };
    readonly CheckBox chkRegistered = new() { Text = "登録済みの語も表示", AutoSize = true };
    readonly Button btnRegister = new() { Text = "IME に登録", AutoSize = true, Font = new Font("Yu Gothic UI", 9f, FontStyle.Bold) };
    readonly Button btnExportIme = new() { Text = "辞書ファイルに出力...", AutoSize = true };
    readonly Button btnGoogle = new() { Text = "Google 日本語入力に登録", AutoSize = true };
    readonly Button btnExportCsv = new() { Text = "CSV に出力...", AutoSize = true };
    readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripStatusLabel counts = new();
    readonly ToolStripProgressBar progressBar = new() { Style = ProgressBarStyle.Marquee, Visible = false };
    readonly ToolTip tip = new();
    readonly CheckBox chkCheckReading = new() { Text = "よみを確かめたい語のみ", AutoSize = true };
    readonly ToolStripButton btnClear = new("一覧を閉じる");
    readonly ToolStripButton btnAddWord = new("語を追加...");
    readonly ToolStripDropDownButton btnBulk = new("一括操作");
    readonly RichTextBox rtbExamples = new()
    {
        Dock = DockStyle.Bottom, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Window,
        DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical,
    };
    readonly Splitter exampleSplitter = new() { Dock = DockStyle.Bottom, Height = 5, BackColor = SystemColors.ControlLight };
    readonly ToolStripDropDownButton btnHelp = new("使い方");

    /// <summary>元に戻す操作（新しいものが上）。各要素は「戻すときに実行する処理」と説明</summary>
    readonly Stack<(string Label, Action Undo)> undo = new();
    /// <summary>途中の状態の自動保存（操作のたびではなく、少し待ってまとめて保存する）</summary>
    readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 1500 };
    /// <summary>よみを直したときに変換結果を取り直す IME（UI スレッドで使う）</summary>
    ImeConverter? uiIme;
    /// <summary>途中保存から再開した一覧の取り込み元の名前（文書そのものは持っていない）</summary>
    List<string> restoredSources = [];

    public MainForm(AppStore store)
    {
        this.store = store;
        Text = "YuWords";
        Font = new Font("Yu Gothic UI", 9f);
        Size = new Size(1150, 720);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);
        Icon = Theme.AppIcon;

        filter.ShowRegistered = store.Settings.ShowRegistered;
        chkRegistered.Checked = filter.ShowRegistered;

        BuildLayout();
        BuildGrid();
        SetStatus("「Outlook から読み込む」で送信メールから候補を集めます。Obsidian のノートや Excel からも読み込めます。");
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveSession(); };
        KeyPreview = true;
        KeyDown += Form_KeyDown;
        ApplyFontSize();
        RestoreLayout();
        RestoreSession();
        CheckForUpdatesOnStartup();
        CheckTeamDictionaryOnStartup();
        if (!store.Settings.Welcomed && store.Registered.Count == 0 && all.Count == 0)
            Shown += async (_, _) => await RestoreHelper.Welcome(this, store);
        UpdateCounts();
    }

    void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F1) { ShowHelp(); e.Handled = true; }
        else if (e.KeyCode == Keys.N && e.Control) { AddWord(); e.Handled = true; }
        else if (e.KeyCode == Keys.F && e.Control) { txtSearch.Focus(); txtSearch.SelectAll(); e.Handled = true; }
    }

    /// <summary>辞書・Outlook・IME が使えるかをまとめて確かめる（IME の COM を使うので STA スレッドで）</summary>
    async Task RunDiagnosticsAsync()
    {
        if (cts != null) return;
        UseWaitCursor = true;
        SetStatus("環境を確かめています...");
        try
        {
            var results = await StaTask.Run(() => Diagnostics.Run(store));
            Log.Info("環境チェック: " + string.Join("、", results.Select(r => $"{r.Name}={r.Status}")));
            SetStatus("環境チェックが終わりました。");
            using var f = new DiagnosticsForm(results, Application.ProductVersion.Split('+')[0], store);
            UseWaitCursor = false;
            f.ShowDialog(this);
        }
        finally { UseWaitCursor = false; }
    }

    void ShowHelp()
    {
        using var f = new HelpForm();
        f.ShowDialog(this);
    }

    /// <summary>語を手で追加する。クリップボードに1行の短い文字があれば最初から入れておく</summary>
    void AddWord()
    {
        if (cts != null) return;   // 読み込み・登録の最中は一覧を変えない
        var clip = "";
        try
        {
            if (Clipboard.ContainsText()) clip = Clipboard.GetText().Trim();
        }
        catch (Exception) { }
        if (clip.Contains('\n') || clip.Length > 60) clip = "";
        using var f = new AddWordForm(MailPreprocessor.Normalize(clip), w => w.Length == 0 ? "" : UiIme.Phonetic(w), r => UiIme.Convert(r));
        if (f.ShowDialog(this) != DialogResult.OK) return;

        var existing = all.FirstOrDefault(i => i.Word == f.Word);
        if (existing != null)
        {
            PushUndo($"「{f.Word}」のよみ", [existing]);
            existing.EditReading(f.Reading);
            (existing.Pos, existing.Register, existing.Reviewed, existing.Held) = (f.Pos, true, true, false);
            Reconvert(existing);
            store.Learn(existing.Source.Word, existing.Reading);
            SetStatus($"「{f.Word}」はすでに一覧にあったので、よみと品詞を更新しました。");
        }
        else
        {
            var c = new Candidate { Word = f.Word, Reading = f.Reading, Category = "手入力", ImeReading = UiIme.Phonetic(f.Word), ReadingSource = "手入力" };
            var item = new ReviewItem(c, store.IsRegistered(f.Word, f.Reading)) { Pos = f.Pos, Reviewed = true };
            item.Register = !item.IsRegistered;
            Reconvert(item);
            all.Insert(0, item);
            PushUndo($"「{f.Word}」を追加", () => all.Remove(item));
            SetStatus($"「{f.Word}」を一覧の先頭に追加しました。「IME に登録」で登録できます。");
        }
        txtSearch.Text = "";
        sortCol = null;
        foreach (DataGridViewColumn col in grid.Columns) col.HeaderCell.SortGlyphDirection = SortOrder.None;
        Refilter();
        ScheduleSave();
        if (view.Count > 0) grid.CurrentCell = grid.Rows[0].Cells[(int)Col.Word];
    }

    /// <summary>設定の文字の大きさを画面と表に反映する</summary>
    ToolStrip tools = null!;
    StatusStrip statusStrip = null!;

    /// <summary>
    /// ツールバー・ステータスバーは画面の文字を受け継がず、拡大率の違うモニターへ移っても大きくならないので、画面と同じ文字にそろえる
    /// </summary>
    void SyncStripFonts()
    {
        tools.Font = Font;
        statusStrip.Font = Font;
    }

    void ApplyFontSize()
    {
        Font = new Font("Yu Gothic UI", store.Settings.FontSize);
        SyncStripFonts();
        if (IsHandleCreated) ApplyColumnWidths(DeviceDpi);
    }

    // ===== 画面の組み立て =====

    void BuildLayout()
    {
        tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2) };
        tools.Items.AddRange([btnOutlook, btnOther, btnAppend, btnCancel, new ToolStripSeparator(), btnSettings, btnDictTool,
            new ToolStripSeparator(), btnClear]);
        btnClear.ToolTipText = "今の一覧を閉じる（途中保存も消す）";
        tools.Items.Insert(tools.Items.IndexOf(btnSettings), btnAddWord);
        tools.Items.Insert(tools.Items.IndexOf(btnSettings), btnBulk);
        BuildBulkMenu();
        tools.Items.Add(btnHelp);
        btnAddWord.ToolTipText = "語を手で追加する（Ctrl+N。クリップボードの語が入ります）";
        btnAddWord.Click += (_, _) => AddWord();
        btnHelp.Alignment = ToolStripItemAlignment.Right;
        btnHelp.DropDownItems.Add("使い方とキー操作（F1）", null, (_, _) => ShowHelp());
        btnHelp.DropDownItems.Add("環境チェック...", null, async (_, _) => await RunDiagnosticsAsync());
        btnHelp.DropDownItems.Add("更新を確認...", null, async (_, _) => await CheckForUpdatesAsync(manual: true));
        btnHelp.DropDownItems.Add("ファイルから更新...", null, async (_, _) => await UpdateFromFileAsync());
        var rollback = new ToolStripMenuItem("前の版に戻す...", null, (_, _) => RollBack());
        btnHelp.DropDownItems.Add(rollback);
        btnHelp.DropDownOpening += (_, _) =>
        {
            var previous = Updater.PreviousVersion();
            rollback.Enabled = previous != null;
            rollback.Text = previous != null ? $"前の版（v{previous}）に戻す..." : "前の版に戻す（控えなし）";
        };
        btnHelp.DropDownItems.Add("ログのフォルダを開く", null, (_, _) =>
        {
            if (Log.Directory != null) Process.Start("explorer.exe", $"\"{Log.Directory}\"");
        });
        btnHelp.DropDownItems.Add(new ToolStripSeparator());
        btnHelp.DropDownItems.Add($"YuWords {Application.ProductVersion.Split('+')[0]}").Enabled = false;
        btnClear.Click += (_, _) => ClearList();
        btnDictTool.ToolTipText = "Microsoft IME のユーザー辞書ツールを開く（登録した語の確認・テキストからの取り込み）";
        btnDictTool.Click += (_, _) => OpenDictionaryTool();
        btnOutlook.ButtonClick += async (_, _) => await LoadAsync(OutlookSource(newOnly: false));
        btnOutlook.DropDownItems.Add(mnuOutlookNew);
        mnuOutlookNew.Click += async (_, _) => await LoadAsync(OutlookSource(newOnly: true));
        btnOutlook.DropDownOpening += (_, _) =>
        {
            var last = store.Settings.LastOutlookRead;
            mnuOutlookNew.Enabled = last != null;
            mnuOutlookNew.Text = last is { } t
                ? $"前回（{t:M/d H:mm}）以降の新しいメールだけ読み込む"
                : "前回以降の新しいメールだけ読み込む（一度読み込むと使えます）";
        };
        btnOther.DropDownItems.Add("フォルダ（Obsidian の Vault・テキスト・Word・PowerPoint）...", null, async (_, _) => await LoadFolderAsync());
        btnOther.DropDownItems.Add("Excel・CSV（用語集・文章の列）...", null, async (_, _) => await LoadTableAsync());
        btnOther.DropDownItems.Add("同僚の辞書（共有辞書）...", null, async (_, _) => await LoadShared());
        btnOther.DropDownItems.Add("チームの辞書の新しい語", null, async (_, _) => await CheckTeamDictionaryAsync(manual: true));
        btnOther.DropDownItems.Add(new ToolStripSeparator());
        btnOther.DropDownItems.Add("メールのテキスト（「---」区切り）...", null, async (_, _) => await LoadMailTextAsync());
        btnCancel.Click += (_, _) => cts?.Cancel();
        btnSettings.Click += (_, _) => OpenSettings();

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 4, 6, 2), WrapContents = true,
        };
        filters.Controls.AddRange([txtSearch, chkUnreviewed, chkUnsure, chkCheckReading, chkSig, chkNotConv, chkChecked, chkRegistered, chkHeld]);
        tip.SetToolTip(chkUnreviewed, "まだ見ていない行だけ（値を変えたり Enter キーを押したりした行は確認済みになります）");
        tip.SetToolTip(chkHeld, "「保留」にした語も表示（保留は次回以降も、選んだよみのまま残ります）");
        tip.SetToolTip(chkCheckReading, "IME のよみと食い違う語、同じよみで表記が違う語（表記ゆれ）だけを表示");
        foreach (Control c in filters.Controls) c.Margin = new Padding(3, 3, 12, 3);
        txtSearch.TextChanged += (_, _) => { filter.Search = txtSearch.Text.Trim(); Refilter(); };
        chkUnsure.CheckedChanged += (_, _) => { filter.UnsureOnly = chkUnsure.Checked; Refilter(); };
        chkSig.CheckedChanged += (_, _) => { filter.SignatureOnly = chkSig.Checked; Refilter(); };
        chkNotConv.CheckedChanged += (_, _) => { filter.NotConvertibleOnly = chkNotConv.Checked; Refilter(); };
        chkChecked.CheckedChanged += (_, _) => { filter.CheckedOnly = chkChecked.Checked; Refilter(); };
        chkCheckReading.CheckedChanged += (_, _) => { filter.CheckReadingOnly = chkCheckReading.Checked; Refilter(); };
        chkUnreviewed.CheckedChanged += (_, _) => { filter.UnreviewedOnly = chkUnreviewed.Checked; Refilter(); };
        chkHeld.CheckedChanged += (_, _) => { filter.ShowHeld = chkHeld.Checked; Refilter(); };
        chkRegistered.CheckedChanged += (_, _) =>
        {
            filter.ShowRegistered = store.Settings.ShowRegistered = chkRegistered.Checked;
            store.SaveSettings();
            Refilter();
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
        };
        buttons.Controls.AddRange([btnRegister, btnGoogle, btnExportIme, btnExportCsv]);
        btnGoogle.Visible = GoogleIme.IsInstalled;   // Google 日本語入力が入っているときだけ出す
        btnGoogle.Click += (_, _) => RegisterGoogle();
        // 下の欄: 左に凡例、右にボタン。高さは中身（文字の大きさ・拡大率）に合わせる
        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(6, 4, 6, 4),
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(BuildLegend(), 0, 0);
        bottom.Controls.Add(buttons, 1, 0);
        btnRegister.Click += async (_, _) => await RegisterAsync();
        btnExportIme.Click += (_, _) => ExportIme();
        btnExportCsv.Click += (_, _) => ExportCsv();

        statusStrip = new StatusStrip();
        statusStrip.Items.AddRange([status, progressBar, counts]);

        grid.Dock = DockStyle.Fill;
        rtbExamples.Height = Dpi.Scale(110);
        Controls.Add(grid);
        // 表の下に、選んだ語の例文を出す欄（境目をドラッグして高さを変えられる）
        Controls.Add(exampleSplitter);
        Controls.Add(rtbExamples);
        Controls.Add(filters);
        Controls.Add(tools);
        Controls.Add(bottom);
        Controls.Add(statusStrip);
    }

    void BuildGrid()
    {
        grid.VirtualMode = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;   // 見出しは1行に
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.None;
        grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        grid.ShowCellToolTips = true;

        void Add(DataGridViewColumn c, Col id, string header, int width, bool readOnly = true)
        {
            c.Name = id.ToString();
            c.HeaderText = header;
            logicalWidths[c] = width;
            c.ReadOnly = readOnly;
            c.SortMode = DataGridViewColumnSortMode.Programmatic;
            grid.Columns.Add(c);
        }
        Add(new DataGridViewCheckBoxColumn(), Col.Register, "登録", 52, readOnly: false);
        Add(new DataGridViewTextBoxColumn { DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } },
            Col.Check, "確認", 52);
        Add(new DataGridViewTextBoxColumn { DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter, ForeColor = Theme.Current.Star } },
            Col.Stars, "おすすめ", 80);
        Add(new DataGridViewTextBoxColumn(), Col.Word, "語句", 150, readOnly: false);
        Add(new DataGridViewTextBoxColumn(), Col.Reading, "よみ", 160, readOnly: false);
        Add(new DataGridViewTextBoxColumn(), Col.ImeReading, "IMEのよみ", 140);
        Add(new DataGridViewTextBoxColumn(), Col.Extra, "別のよみ", 90, readOnly: false);
        var pos = new DataGridViewComboBoxColumn { FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing };
        pos.Items.AddRange(ReviewItem.PosChoices);
        Add(pos, Col.Pos, "品詞", 70, readOnly: false);
        Add(new DataGridViewTextBoxColumn { DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } },
            Col.MailCount, "出現メール数", 96);
        Add(new DataGridViewTextBoxColumn(), Col.Category, "分類", 64);
        Add(new DataGridViewTextBoxColumn { DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } },
            Col.Signature, "署名", 64);
        Add(new DataGridViewTextBoxColumn(), Col.Ime, "IME変換結果", 130);
        Add(new DataGridViewTextBoxColumn { DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } },
            Col.Unsure, "要確認", 56);
        Add(new DataGridViewTextBoxColumn { AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            Col.Example, "例文", 300);

        grid.CellValueNeeded += Grid_CellValueNeeded;
        grid.CellValuePushed += Grid_CellValuePushed;
        grid.CellFormatting += Grid_CellFormatting;
        grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
                ShowColumnMenu(grid.PointToClient(Cursor.Position));
            else
                SortBy((Col)e.ColumnIndex);
        };
        grid.ColumnWidthChanged += (_, e) => RememberColumnWidth(e.Column);
        // チェックボックスとコンボは、クリックした時点で値を確定させる
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.DataError += (_, e) => e.ThrowException = false;
        grid.KeyDown += Grid_KeyDown;
        grid.CurrentCellChanged += (_, _) => ShowExamples();
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < view.Count && e.ColumnIndex == (int)Col.ImeReading)
                AdoptImeReading([view[e.RowIndex]]);
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("選択行を登録 ON", null, (_, _) => SetRegister(true));
        menu.Items.Add("選択行を登録 OFF", null, (_, _) => SetRegister(false));
        menu.Items.Add("IME のよみを採用", null, (_, _) => AdoptImeReading(SelectedItems()));
        menu.Items.Add("短いよみを付ける", null, (_, _) => AddShortReadings());
        menu.Items.Add("このよみをルールにする", null, (_, _) => MakeRules());
        menu.Items.Add("チームの辞書に足す", null, async (_, _) => await AddToTeamAsync(SelectedItems().SelectMany(i => i.Entries()).Select(e => (e.Word, e.Reading, e.Pos))));
        var posMenu = new ToolStripMenuItem("品詞を設定");
        foreach (var p in ReviewItem.PosChoices) posMenu.DropDownItems.Add(p, null, (_, _) => SetPos(p));
        menu.Items.Add(posMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("確認済みにする（Enter）", null, (_, _) => SetReviewed(true));
        menu.Items.Add("未確認に戻す", null, (_, _) => SetReviewed(false));
        menu.Items.Add("保留にする（次回以降も保留）", null, (_, _) => SetHeld(true));
        menu.Items.Add("保留を解除", null, (_, _) => SetHeld(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("除外リストに追加（Delete）", null, (_, _) => ExcludeSelected());
        menu.Items.Add(new ToolStripSeparator());
        var undoItem = new ToolStripMenuItem("元に戻す", null, (_, _) => Undo()) { ShortcutKeyDisplayString = "Ctrl+Z" };
        menu.Items.Add(undoItem);
        menu.Opening += (_, _) =>
        {
            undoItem.Enabled = undo.Count > 0;
            undoItem.Text = undo.Count > 0 ? $"元に戻す: {undo.Peek().Label}" : "元に戻す";
        };
        grid.ContextMenuStrip = menu;
        // 右クリックした行が選択に入っていなければ、その行だけを選び直す
        grid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            if (!grid.Rows[e.RowIndex].Cells.Cast<DataGridViewCell>().Any(c => c.Selected))
            {
                grid.ClearSelection();
                grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(e.ColumnIndex, 0)];
            }
        };
    }

    /// <summary>列の幅（拡大率 100% のときのピクセル）。列の幅は自動では拡大されないので、拡大率に合わせて設定し直す</summary>
    readonly Dictionary<DataGridViewColumn, int> logicalWidths = [];

    bool applyingWidths;

    void ApplyColumnWidths(int dpi)
    {
        // 文字を大きくしたら、列の幅と行の高さも同じ割合で広げる
        var f = store.Settings.FontSize / 9f;
        int S(int px) => (int)(px * f * dpi / 96);
        applyingWidths = true;
        grid.RowHeadersWidth = 24 * dpi / 96;
        grid.RowTemplate.Height = S(24);
        foreach (var (c, w) in logicalWidths)
        {
            if (c.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill) c.MinimumWidth = S(120);
            else c.Width = S(store.Settings.Ui.ColumnWidths.GetValueOrDefault(c.Name, w));   // 利用者が変えた幅を優先
        }
        applyingWidths = false;
    }

    // ===== 見た目の保存 =====

    /// <summary>色の意味の凡例（下の欄の左）</summary>
    Control BuildLegend()
    {
        var t = Theme.Current;
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
        void Item(string text, Color? back = null, Color? fore = null)
        {
            panel.Controls.Add(new Label
            {
                Text = text, AutoSize = true, Padding = new Padding(4, 2, 4, 2), Margin = new Padding(0, 0, 6, 0),
                BackColor = back ?? Color.Transparent, ForeColor = fore ?? t.Fore,
            });
        }
        Item("推測のよみ", t.Unsure);
        Item("よみ不明", t.Unknown);
        Item("IMEと違う", t.ImeDiff);
        Item("表記ゆれ", t.Variant);
        Item("変換できる", fore: t.Good);
        Item("登録済み", fore: t.Muted);
        return panel;
    }

    /// <summary>前回のウィンドウの位置・大きさ、例文欄の高さ、隠した列を戻す</summary>
    void RestoreLayout()
    {
        var ui = store.Settings.Ui;
        if (ui.Width > 0 && ui.Height > 0)
        {
            var rect = new Rectangle(ui.X, ui.Y, Dpi.Scale(ui.Width), Dpi.Scale(ui.Height));
            // 前回のモニターが外されていたら、位置は戻さない（画面の外に出ないように）
            if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(rect)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = rect;
            }
            if (ui.Maximized) WindowState = FormWindowState.Maximized;
        }
        if (ui.ExampleHeight > 0) rtbExamples.Height = Dpi.Scale(ui.ExampleHeight);
        foreach (DataGridViewColumn c in grid.Columns)
            c.Visible = !ui.HiddenColumns.Contains(c.Name);
    }

    void SaveLayout()
    {
        var ui = store.Settings.Ui;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        (ui.X, ui.Y) = (b.X, b.Y);
        (ui.Width, ui.Height) = (b.Width * 96 / Dpi.Scale(96), b.Height * 96 / Dpi.Scale(96));
        ui.Maximized = WindowState == FormWindowState.Maximized;
        ui.ExampleHeight = rtbExamples.Height * 96 / Dpi.Scale(96);
        ui.HiddenColumns = grid.Columns.Cast<DataGridViewColumn>().Where(c => !c.Visible).Select(c => c.Name).ToList();
        try { store.SaveSettings(); } catch (Exception) { }
    }

    /// <summary>列の見出しを右クリックしたときの、列の表示・非表示のメニュー</summary>
    void ShowColumnMenu(Point at)
    {
        var menu = new ContextMenuStrip();
        foreach (DataGridViewColumn c in grid.Columns)
        {
            // 登録・語句・よみは消すと作業できないので、いつも出す
            if ((Col)c.Index is Col.Register or Col.Word or Col.Reading) continue;
            var col = c;
            menu.Items.Add(new ToolStripMenuItem(c.HeaderText, null, (_, _) =>
            {
                col.Visible = !col.Visible;
                SaveLayout();
            }) { Checked = c.Visible });
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("列の幅を元に戻す", null, (_, _) =>
        {
            store.Settings.Ui.ColumnWidths.Clear();
            ApplyColumnWidths(DeviceDpi);
            SaveLayout();
        });
        menu.Show(grid, at);
    }

    /// <summary>利用者が列の幅を変えたら覚える（拡大率 100%・文字 9pt のときの幅にして）</summary>
    void RememberColumnWidth(DataGridViewColumn c)
    {
        if (applyingWidths || !logicalWidths.ContainsKey(c) || c.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill) return;
        var scale = store.Settings.FontSize / 9f * DeviceDpi / 96f;
        store.Settings.Ui.ColumnWidths[c.Name] = (int)Math.Round(c.Width / scale);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyColumnWidths(DeviceDpi);
    }

    // 拡大率の違うモニターへ移ったとき
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        SyncStripFonts();
        ApplyColumnWidths(e.DeviceDpiNew);
        Refilter();   // 行の高さは行を作り直したときに効く
    }

    // ===== 設定 =====

    void OpenSettings()
    {
        using var dlg = new SettingsForm(store);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        // 除外リストが増えていたら、いまの一覧からも外す
        all.RemoveAll(i => store.Excluded.Contains(i.Source.Word));
        RefreshRegistered();
        Refilter();
        ScheduleSave();
        ApplyFontSize();
        SetStatus("設定を保存しました。");
        if (loaded.Any(l => l.Docs.Count > 0) &&
            MessageBox.Show(this, "今の一覧を、新しい設定（出現数・ストップワード・よみのルールなど）で数え直しますか？\n読み込み直さないのですぐ終わります。選択は引き継ぎます。",
                "設定", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            _ = RecountAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 一覧と選択は途中保存されるので、次回起動したときに続きから再開できる
        cts?.Cancel();
        saveTimer.Stop();
        SaveLayout();
        SaveSession();
        uiIme?.Dispose();
        base.OnFormClosing(e);
    }
}
