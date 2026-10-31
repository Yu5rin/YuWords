using YuWords.Core;

namespace YuWords;

/// <summary>Excel・CSV から取り込むシートと列を選ぶ画面</summary>
sealed class TableColumnForm : Form
{
    readonly List<DocumentSources.Sheet> sheets;
    readonly ComboBox cmbSheet = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.Scale(300) };
    readonly ComboBox cmbColumn = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.Scale(300) };
    readonly CheckBox chkHeader = new() { Text = "1行目は見出し（取り込まない）", AutoSize = true, Checked = true };
    readonly TextBox txtPreview = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, WordWrap = false,
    };
    readonly Label lblCount = new() { AutoSize = true };
    readonly Button ok = new() { Text = "読み込む", DialogResult = DialogResult.OK, AutoSize = true };

    public DocumentSources.Sheet Sheet => sheets[cmbSheet.SelectedIndex];
    public int Column => cmbColumn.SelectedIndex;
    public bool SkipHeader => chkHeader.Checked;
    public string ColumnLabel => sheets.Count > 1 ? $"{Sheet.Name} シートの {DocumentSources.ColumnName(Column)} 列" : $"{DocumentSources.ColumnName(Column)} 列";

    public TableColumnForm(string fileName, List<DocumentSources.Sheet> sheets)
    {
        this.sheets = sheets;
        Text = $"取り込む列を選ぶ - {fileName}";
        Font = new Font("Yu Gothic UI", 9f);
        Size = new Size(520, 480);
        MinimumSize = new Size(420, 360);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 10, 3) });
            t.Controls.Add(c);
        }
        Row("シート", cmbSheet);
        Row("列", cmbColumn);
        t.Controls.Add(new Label());
        t.Controls.Add(chkHeader);
        var note = new Label
        {
            Text = "選んだ列のセルを1つずつ文書として扱います（出現数は「その語が出た行の数」）。中身の例:",
            AutoSize = true, MaximumSize = new Size(Dpi.Scale(470), 0), Margin = new Padding(3, 8, 3, 3),
        };
        t.Controls.Add(note);
        t.SetColumnSpan(note, 2);
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.Controls.Add(txtPreview);
        t.SetColumnSpan(txtPreview, 2);
        t.Controls.Add(lblCount);
        t.SetColumnSpan(lblCount, 2);

        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([cancel, ok]);
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(t);
        Controls.Add(buttons);

        foreach (var s in sheets) cmbSheet.Items.Add($"{s.Name}（{s.Rows.Count} 行）");
        cmbSheet.SelectedIndexChanged += (_, _) => FillColumns();
        cmbColumn.SelectedIndexChanged += (_, _) => UpdatePreview();
        chkHeader.CheckedChanged += (_, _) => { FillColumns(); };
        if (sheets.Count > 0) cmbSheet.SelectedIndex = 0;
        else ok.Enabled = false;
    }

    void FillColumns()
    {
        var keep = Math.Max(cmbColumn.SelectedIndex, -1);
        cmbColumn.Items.Clear();
        var header = Sheet.Rows.FirstOrDefault() ?? [];
        for (int c = 0; c < Sheet.ColumnCount; c++)
        {
            var name = DocumentSources.ColumnName(c);
            var label = SkipHeader && c < header.Length && header[c].Trim().Length > 0 ? $"{name}: {header[c].Trim()}" : name;
            cmbColumn.Items.Add(label);
        }
        if (cmbColumn.Items.Count == 0) { ok.Enabled = false; txtPreview.Text = ""; lblCount.Text = "空のシートです。"; return; }
        // 既定は、文章がいちばん長い列（本文・件名などを選びやすく）
        cmbColumn.SelectedIndex = keep >= 0 && keep < cmbColumn.Items.Count ? keep : LongestColumn();
    }

    int LongestColumn()
    {
        var best = 0;
        double bestLen = -1;
        for (int c = 0; c < Sheet.ColumnCount; c++)
        {
            var cells = Sheet.Rows.Skip(SkipHeader ? 1 : 0).Take(200).Where(r => c < r.Length).Select(r => r[c]).ToList();
            var len = cells.Count == 0 ? 0 : cells.Average(x => double.TryParse(x, out _) ? 0 : x.Length);
            if (len > bestLen) { bestLen = len; best = c; }
        }
        return best;
    }

    void UpdatePreview()
    {
        if (cmbColumn.SelectedIndex < 0) return;
        var docs = DocumentSources.ColumnDocuments(Sheet, Column, SkipHeader);
        txtPreview.Text = string.Join("\r\n", docs.Take(30).Select(d => string.Join(" / ", d)));
        lblCount.Text = $"取り込む行: {docs.Count} 行";
        ok.Enabled = docs.Count > 0;
    }
}
