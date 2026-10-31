using YuWords.Core;

namespace YuWords;

/// <summary>環境チェックの結果を一覧で見せ、文字としてコピーできる画面</summary>
sealed class DiagnosticsForm : Form
{
    readonly AppStore store;

    public DiagnosticsForm(List<CheckResult> results, string version, AppStore store)
    {
        this.store = store;
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
        var selfTest = new Button { Text = "試しに登録して確かめる", AutoSize = true, Enabled = ImeRegistration.Enabled };
        new ToolTip().SetToolTip(selfTest, $"架空の語「{ImeWorker.TestWord}」を IME に登録し、入ったかを確かめてから消します");
        selfTest.Click += async (_, _) => await RunSelfTestAsync(selfTest);
        var copy = new Button { Text = "結果をコピー", AutoSize = true };
        copy.Click += (_, _) => { Clipboard.SetText(text); copy.Text = "コピーしました"; };
        var logs = new Button { Text = "ログのフォルダを開く", AutoSize = true, Enabled = Log.Directory != null };
        logs.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", $"\"{Log.Directory}\"");
        var close = new Button { Text = "閉じる", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([close, copy, logs, selfTest]);
        var note = new Label
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 4),
            Text = "IME のユーザー辞書は開いて読むだけで、何も登録しません。Outlook は起動しません。",
        };
        Controls.Add(list);
        void AddSelfTestLine(string text) => list.Items.Add(new ListViewItem([text.StartsWith("OK") ? "OK" : text.StartsWith("NG") ? "NG" : "注意", "試しに登録", text]));
        addSelfTest = AddSelfTestLine;
        Controls.Add(note);
        Controls.Add(buttons);
        CancelButton = close;
    }

    Action<string>? addSelfTest;

    /// <summary>架空の語を登録・読み直し・削除して、直接登録がこの PC で効くかを確かめる（別のプロセスで行う）</summary>
    async Task RunSelfTestAsync(Button button)
    {
        button.Enabled = false;
        UseWaitCursor = true;
        string result;
        try { result = await StaTask.Run(ImeRegistration.SelfTest); }
        catch (InvalidOperationException e) { result = "NG: " + e.Message; }
        finally { UseWaitCursor = false; }
        Log.Info($"試しに登録: {result}");
        addSelfTest?.Invoke(result);
        if (result.StartsWith("NG") &&
            MessageBox.Show(this, $"{result}\n\nIME のユーザー辞書を直接使う機能を OFF にしますか？（登録は辞書ファイルに出力して、ユーザー辞書ツールで取り込む流れになります）",
                "試しに登録", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            store.Settings.UseImeDictionary = ImeRegistration.Enabled = false;
            store.SaveSettings();
        }
        else if (!result.StartsWith("NG"))
            MessageBox.Show(this, result, "試しに登録", MessageBoxButtons.OK, result.StartsWith("OK") ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        button.Enabled = true;
    }
}
