using YuWords.Core;

namespace YuWords;

/// <summary>語を手で追加する画面。クリップボードの語を最初から入れておき、よみは IME に問い合わせて提案する</summary>
sealed class AddWordForm : Form
{
    readonly TextBox txtWord = new() { Width = Dpi.Scale(320) };
    readonly TextBox txtReading = new() { Width = Dpi.Scale(320) };
    readonly ComboBox cmbPos = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.Scale(140) };
    readonly Label lblConv = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    readonly Func<string, string> phonetic;
    readonly Func<string, string> convert;
    bool readingEdited;

    public string Word => txtWord.Text.Trim();
    public string Reading => TermExtractor.KataToHira(txtReading.Text.Trim());
    public string Pos => (string)cmbPos.SelectedItem!;

    public AddWordForm(string initialWord, Func<string, string> phonetic, Func<string, string> convert)
    {
        this.phonetic = phonetic;
        this.convert = convert;
        Text = "語を追加";
        Font = new Font("Yu Gothic UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;

        cmbPos.Items.AddRange(ReviewItem.PosChoices);
        cmbPos.SelectedIndex = 0;

        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 10, 3) });
            t.Controls.Add(c);
        }
        Row("語句", txtWord);
        Row("よみ", txtReading);
        Row("品詞", cmbPos);
        t.Controls.Add(new Label());
        t.Controls.Add(lblConv);

        var ok = new Button { Text = "一覧に追加", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([cancel, ok]);
        t.Controls.Add(new Label());
        t.Controls.Add(buttons);
        Controls.Add(t);
        AcceptButton = ok;
        CancelButton = cancel;

        txtWord.TextChanged += (_, _) =>
        {
            // よみを自分で入れるまでは、語句に合わせて IME のよみを提案し続ける
            if (!readingEdited) txtReading.Text = phonetic(Word);
            UpdateState();
        };
        txtReading.TextChanged += (_, _) => UpdateState();
        txtReading.KeyPress += (_, _) => readingEdited = true;
        ok.Click += (_, e) =>
        {
            if (Word.Length == 0 || Word.Length > 60 || Reading.Length == 0)
            {
                MessageBox.Show(this, "語句（60文字まで）とよみを入れてください。", Text);
                DialogResult = DialogResult.None;
            }
        };
        txtWord.Text = initialWord;
        UpdateState();
    }

    void UpdateState()
    {
        if (Reading.Length == 0) { lblConv.Text = ""; return; }
        var c = convert(Reading);
        lblConv.Text = c == Word ? "このよみで、すでに IME が変換できます（登録すると予測変換に出やすくなります）" : $"いまの IME の変換: {c}";
    }
}
