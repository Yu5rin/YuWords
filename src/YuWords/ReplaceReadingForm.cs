namespace YuWords;

/// <summary>よみの一部をまとめて置き換える画面（かかり → がかり など）</summary>
sealed class ReplaceReadingForm : Form
{
    readonly TextBox txtFrom = new() { Width = Dpi.Scale(160) };
    readonly TextBox txtTo = new() { Width = Dpi.Scale(160) };

    public string From => txtFrom.Text.Trim();
    public string To => txtTo.Text.Trim();

    public ReplaceReadingForm(int count)
    {
        Text = "よみを置き換える";
        Font = new Font("Yu Gothic UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;

        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
        var note = new Label { Text = $"{count} 行のよみの中の文字を置き換えます（カタカナはひらがなにします）。", AutoSize = true, Margin = new Padding(3, 3, 3, 10) };
        t.Controls.Add(note);
        t.SetColumnSpan(note, 2);
        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 10, 3) });
            t.Controls.Add(c);
        }
        Row("置き換える前", txtFrom);
        Row("置き換えた後", txtTo);
        var ok = new Button { Text = "置き換える", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([cancel, ok]);
        t.Controls.Add(new Label());
        t.Controls.Add(buttons);
        Controls.Add(t);
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, _) =>
        {
            if (From.Length == 0) DialogResult = DialogResult.None;
        };
    }
}
