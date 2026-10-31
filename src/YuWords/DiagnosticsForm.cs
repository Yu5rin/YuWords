using YuWords.Core;

namespace YuWords;

/// <summary>環境チェックの結果を一覧で見せ、文字としてコピーできる画面</summary>
sealed class DiagnosticsForm : Form
{
    public DiagnosticsForm(List<CheckResult> results, string version)
    {
        Text = "環境チェック";
        Font = new Font("Yu Gothic UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);
        Size = new Size(Dpi.Scale(760), Dpi.Scale(380));
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        list.Columns.Add("結果", Dpi.Scale(60));
        list.Columns.Add("項目", Dpi.Scale(200));
        list.Columns.Add("詳細", Dpi.Scale(460));
        foreach (var r in results)
        {
            var item = new ListViewItem([r.Status switch { CheckStatus.OK => "OK", CheckStatus.Warning => "注意", _ => "NG" }, r.Name, r.Detail]);
            item.ForeColor = r.Status switch
            {
                CheckStatus.OK => Color.FromArgb(0, 120, 0),
                CheckStatus.Warning => Color.FromArgb(180, 110, 0),
                _ => Color.FromArgb(200, 0, 0),
            };
            list.Items.Add(item);
        }

        var text = Diagnostics.Format(results, version);
        var copy = new Button { Text = "結果をコピー", AutoSize = true };
        copy.Click += (_, _) => { Clipboard.SetText(text); copy.Text = "コピーしました"; };
        var logs = new Button { Text = "ログのフォルダを開く", AutoSize = true, Enabled = Log.Directory != null };
        logs.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", $"\"{Log.Directory}\"");
        var close = new Button { Text = "閉じる", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([close, copy, logs]);
        var note = new Label
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 4),
            Text = "IME のユーザー辞書は開いて読むだけで、何も登録しません。Outlook は起動しません。",
        };
        Controls.Add(list);
        Controls.Add(note);
        Controls.Add(buttons);
        CancelButton = close;
    }
}
