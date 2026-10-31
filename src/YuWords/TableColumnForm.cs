using YuWords.Core;

namespace YuWords;

/// <summary>
/// Excel・CSV から取り込む方法・シート・列を選ぶ画面。
/// 用語集（1行に語句とよみ）ならその組をそのまま候補にし、文章の列なら語を抽出する
/// </summary>
sealed class TableColumnForm : Form
{
    readonly List<DocumentSources.Sheet> sheets;
    readonly RadioButton rdoGlossary = new() { Text = "用語集として取り込む（1行に1語。よみの列があれば使う）", AutoSize = true };
    readonly RadioButton rdoText = new() { Text = "文章として語を抽出する（件名・本文・メモなどの列）", AutoSize = true };
    readonly ComboBox cmbSheet = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.Scale(300) };
    readonly NumericUpDown numHeader = new() { Minimum = 0, Maximum = 1000, Width = Dpi.Scale(70) };
    readonly ComboBox cmbColumn = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.Scale(300) };
    readonly ComboBox cmbReading = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.Scale(300) };
    readonly Label lblReading = new() { Text = "よみの列", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 10, 3) };
    readonly ComboBox cmbNote = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.Scale(300) };
    readonly Label lblNote = new() { Text = "意味の列", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 10, 3) };
    readonly TextBox txtPreview = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, WordWrap = false,
    };
    readonly Label lblCount = new() { AutoSize = true };
    readonly Button ok = new() { Text = "読み込む", DialogResult = DialogResult.OK, AutoSize = true };
    bool filling;

    public DocumentSources.Sheet Sheet => sheets[cmbSheet.SelectedIndex];
    public bool Glossary => rdoGlossary.Checked;
    /// <summary>語句の列（用語集）または文章の列</summary>
    public int Column => cmbColumn.SelectedIndex;
    /// <summary>よみの列（無ければ -1）</summary>
    public int ReadingColumn => cmbReading.SelectedIndex - 1;
    /// <summary>意味・説明の列（無ければ -1）。例文の欄に出す</summary>
    public int NoteColumn => cmbNote.SelectedIndex - 1;
    /// <summary>見出しの行（0始まり。見出しが無ければ -1）。これより下を取り込む</summary>
    public int HeaderRow => (int)numHeader.Value - 1;
    public string ColumnLabel => sheets.Count > 1 ? $"{Sheet.Name} シートの {DocumentSources.ColumnName(Column)} 列" : $"{DocumentSources.ColumnName(Column)} 列";

    /// <summary>文章として抽出するときの文書（見出しより下の、選んだ列のセル）</summary>
    public List<List<string>> Documents() =>
        DocumentSources.ColumnDocuments(new DocumentSources.Sheet(Sheet.Name, Sheet.Rows.Skip(HeaderRow + 1).ToList()), Column, false);

    /// <summary>用語集として取り込むときの語句とよみ</summary>
    public List<DocumentSources.GlossaryEntry> Entries() =>
        DocumentSources.GlossaryEntries(Sheet, Column, ReadingColumn, HeaderRow, NoteColumn);

    public TableColumnForm(string fileName, List<DocumentSources.Sheet> sheets)
    {
        this.sheets = sheets;
        Text = $"取り込み方と列を選ぶ - {fileName}";
        Font = new Font("Yu Gothic UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);
        Size = new Size(Dpi.Scale(560), Dpi.Scale(560));
        MinimumSize = new Size(Dpi.Scale(460), Dpi.Scale(420));
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int rows = 0;
        void Span(Control c) { t.Controls.Add(c, 0, rows++); t.SetColumnSpan(c, 2); }
        void Row(Control label, Control c) { t.Controls.Add(label, 0, rows); t.Controls.Add(c, 1, rows++); }
        Label L(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 10, 3) };

        Span(rdoGlossary);
        Span(rdoText);
        Row(L("シート"), cmbSheet);
        var header = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
        header.Controls.AddRange([numHeader, new Label { Text = "行目（0 なら見出しなし。これより下を取り込む）", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }]);
        Row(L("見出しの行"), header);
        Row(L("語句の列"), cmbColumn);
        Row(lblReading, cmbReading);
        Row(lblNote, cmbNote);
        Span(new Label { Text = "中身の例:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        for (int i = 0; i < rows; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Span(txtPreview);
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Span(lblCount);

        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([cancel, ok]);
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(t);
        Controls.Add(buttons);

        foreach (var s in sheets) cmbSheet.Items.Add($"{s.Name}（{s.Rows.Count} 行）");
        cmbSheet.SelectedIndexChanged += (_, _) => SheetChanged();
        numHeader.ValueChanged += (_, _) => { if (!filling) FillColumns(keepSelection: true); };
        cmbColumn.SelectedIndexChanged += (_, _) => UpdatePreview();
        cmbReading.SelectedIndexChanged += (_, _) => UpdatePreview();
        cmbNote.SelectedIndexChanged += (_, _) => UpdatePreview();
        rdoGlossary.CheckedChanged += (_, _) => { if (!filling) { UpdateMode(); UpdatePreview(); } };
        if (sheets.Count > 0) cmbSheet.SelectedIndex = 0;
        else ok.Enabled = false;
    }

    /// <summary>シートを選んだら、見出しの行と取り込み方・列を推し量って入れる</summary>
    void SheetChanged()
    {
        filling = true;
        int headerRow = DocumentSources.FindHeaderRow(Sheet);
        var (word, reading) = DocumentSources.GuessGlossaryColumns(Sheet, headerRow);
        numHeader.Maximum = Math.Max(Sheet.Rows.Count, 1);
        numHeader.Value = headerRow >= 0 ? headerRow + 1 : Math.Min(1, numHeader.Maximum);
        (word >= 0 ? rdoGlossary : rdoText).Checked = true;
        FillColumns(keepSelection: false);
        if (word >= 0 && word < cmbColumn.Items.Count) cmbColumn.SelectedIndex = word;
        if (reading >= 0 && reading + 1 < cmbReading.Items.Count) cmbReading.SelectedIndex = reading + 1;
        int note = DocumentSources.GuessNoteColumn(Sheet, headerRow);
        if (note >= 0 && note + 1 < cmbNote.Items.Count) cmbNote.SelectedIndex = note + 1;
        filling = false;
        UpdateMode();
        UpdatePreview();
    }

    void UpdateMode()
    {
        lblReading.Visible = cmbReading.Visible = lblNote.Visible = cmbNote.Visible = Glossary;
        ok.Text = Glossary ? "取り込む" : "読み込む";
    }

    void FillColumns(bool keepSelection)
    {
        int keepCol = cmbColumn.SelectedIndex, keepRead = cmbReading.SelectedIndex, keepNote = cmbNote.SelectedIndex;
        cmbColumn.Items.Clear();
        cmbReading.Items.Clear();
        cmbNote.Items.Clear();
        cmbReading.Items.Add("（よみの列なし。IME に問い合わせる）");
        cmbNote.Items.Add("（なし）");
        var header = HeaderRow >= 0 && HeaderRow < Sheet.Rows.Count ? Sheet.Rows[HeaderRow] : [];
        for (int c = 0; c < Sheet.ColumnCount; c++)
        {
            var name = DocumentSources.ColumnName(c);
            var label = c < header.Length && header[c].Trim().Length > 0 ? $"{name}: {header[c].Trim()}" : name;
            cmbColumn.Items.Add(label);
            cmbReading.Items.Add(label);
            cmbNote.Items.Add(label);
        }
        if (cmbColumn.Items.Count == 0) { ok.Enabled = false; txtPreview.Text = ""; lblCount.Text = "空のシートです。"; return; }
        cmbColumn.SelectedIndex = keepSelection && keepCol >= 0 && keepCol < cmbColumn.Items.Count ? keepCol : LongestColumn();
        cmbReading.SelectedIndex = keepSelection && keepRead >= 0 && keepRead < cmbReading.Items.Count ? keepRead : 0;
        cmbNote.SelectedIndex = keepSelection && keepNote >= 0 && keepNote < cmbNote.Items.Count ? keepNote : 0;
        UpdatePreview();
    }

    /// <summary>文章がいちばん長い列（本文・件名などを選びやすく）</summary>
    int LongestColumn()
    {
        var best = 0;
        double bestLen = -1;
        for (int c = 0; c < Sheet.ColumnCount; c++)
        {
            var cells = Sheet.Rows.Skip(HeaderRow + 1).Take(200).Where(r => c < r.Length).Select(r => r[c]).ToList();
            var len = cells.Count == 0 ? 0 : cells.Average(x => double.TryParse(x, out _) ? 0 : x.Length);
            if (len > bestLen) { bestLen = len; best = c; }
        }
        return best;
    }

    void UpdatePreview()
    {
        if (filling || cmbColumn.SelectedIndex < 0) return;
        if (Glossary)
        {
            var e = Entries();
            txtPreview.Text = string.Join("\r\n", e.Take(30).Select(x =>
                (x.Reading.Length > 0 ? $"{x.Word}（{x.Reading}）" : $"{x.Word}（よみなし）") +
                (x.Extra.Length > 0 ? $"［別のよみ {x.Extra}］" : "") + (x.Adjusted ? "［語句を直した］" : "") +
                (x.Note.Length > 0 ? $" … {(x.Note.Length > 30 ? x.Note[..30] + "…" : x.Note)}" : "")));
            int noReading = e.Count(x => x.Reading.Length == 0), adjusted = e.Count(x => x.Adjusted);
            lblCount.Text = $"取り込む語: {e.Count} 語" + (noReading > 0 ? $"（よみなし {noReading} 語は IME に問い合わせ、要確認にします）" : "") +
                            (adjusted > 0 ? $"（「英字＋説明」の {adjusted} 語は語句を英字だけにし、要確認にします）" : "");
            ok.Enabled = e.Count > 0;
        }
        else
        {
            var docs = Documents();
            txtPreview.Text = string.Join("\r\n", docs.Take(30).Select(d => string.Join(" / ", d)));
            lblCount.Text = $"取り込む行: {docs.Count} 行（出現数は「その語が出た行の数」）";
            ok.Enabled = docs.Count > 0;
        }
    }
}
