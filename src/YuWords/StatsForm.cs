using YuWords.Core;

namespace YuWords;

/// <summary>これまでの効果（登録した語の数・月ごとの推移・品詞・よく使う登録語）を見せる画面</summary>
sealed class StatsForm : Form
{
    readonly UsageStats stats;

    public StatsForm(UsageStats stats)
    {
        this.stats = stats;
        Text = "これまでの効果";
        Font = new Font("Yu Gothic UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);
        Size = new Size(Dpi.Scale(680), Dpi.Scale(600));
        MinimumSize = new Size(Dpi.Scale(520), Dpi.Scale(480));
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(12) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.Scale(170)));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var summary = new Label { AutoSize = true, Font = new Font("Yu Gothic UI", 11f), Margin = new Padding(3, 3, 3, 10), Text = SummaryText() };
        t.Controls.Add(summary, 0, 0);
        t.SetColumnSpan(summary, 2);

        t.Controls.Add(Caption("月ごとに登録した語（直近 12 か月）"), 0, 1);
        t.SetColumnSpan(t.GetControlFromPosition(0, 1)!, 2);
        var chart = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Current.Back };
        chart.Paint += (_, e) => DrawMonths(e.Graphics, chart.ClientRectangle);
        chart.Resize += (_, _) => chart.Invalidate();
        t.Controls.Add(chart, 0, 2);
        t.SetColumnSpan(chart, 2);

        var pos = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        pos.Columns.Add("品詞", Dpi.Scale(120));
        pos.Columns.Add("語の数", Dpi.Scale(70), HorizontalAlignment.Right);
        foreach (var (p, n) in stats.ByPos) pos.Items.Add(new ListViewItem([p, n.ToString()]));
        var used = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
        used.Columns.Add("よく使う登録語", Dpi.Scale(200));
        used.Columns.Add("出たメール", Dpi.Scale(80), HorizontalAlignment.Right);
        foreach (var (w, n) in stats.TopUsed) used.Items.Add(new ListViewItem([w, $"{n} 通"]));
        t.Controls.Add(Box("品詞ごと", pos), 0, 3);
        t.Controls.Add(Box(stats.RecentMails > 0
            ? $"前回読み込んだ送信メール（{stats.RecentMails} 通）でよく使った登録語"
            : "よく使う登録語（Outlook の送信メールを読み込むと出ます）", used), 1, 3);

        var close = new Button { Text = "閉じる", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.Add(close);
        CancelButton = close;
        Controls.Add(t);
        Controls.Add(buttons);
    }

    string SummaryText()
    {
        if (stats.Total == 0) return "まだ登録した語がありません。語を登録すると、ここに効果が出ます。";
        var lines = new List<string>
        {
            $"これまでに登録した語: {stats.Total} 語" + (stats.FirstRegistered is { } f ? $"（{f:yyyy/MM/dd} から）" : "") +
            (stats.Google > 0 ? $"　Microsoft IME {stats.MicrosoftIme} 語・Google 日本語入力 {stats.Google} 語" : ""),
        };
        if (stats.Rescued > 0) lines.Add($"そのうち、登録する前は IME で変換できなかった語: {stats.Rescued} 語");
        if (stats.RecentMails > 0)
            lines.Add($"前回の送信メール（{stats.RecentAt:yyyy/MM/dd}、{stats.RecentMails} 通）で使った登録語: {stats.UsedInRecent} 語");
        if (stats.Stale > 0) lines.Add($"長いあいだ使っていないかもしれない語: {stats.Stale} 語（［設定］→［登録済みの語］で確かめられます）");
        return string.Join("\n", lines);
    }

    static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 3), ForeColor = Theme.Current.Muted };

    static Control Box(string caption, Control content)
    {
        var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(3, 8, 3, 3) };
        p.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        p.Controls.Add(Caption(caption));
        p.Controls.Add(content);
        return p;
    }

    /// <summary>月ごとの登録数の棒グラフ</summary>
    void DrawMonths(Graphics g, Rectangle r)
    {
        var months = stats.ByMonth;
        if (months.Count == 0) return;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var font = new Font("Yu Gothic UI", 8f);
        using var fore = new SolidBrush(Theme.Current.Fore);
        using var muted = new SolidBrush(Theme.Current.Muted);
        using var bar = new SolidBrush(Color.FromArgb(70, 130, 220));
        int max = Math.Max(1, months.Max(m => m.Count));
        int labelH = (int)g.MeasureString("2026/10", font).Height + 2;
        int top = labelH, bottom = r.Height - labelH - 2;
        float slot = (float)r.Width / months.Count;
        for (int i = 0; i < months.Count; i++)
        {
            var (month, n) = months[i];
            float h = (bottom - top) * n / (float)max;
            float x = i * slot + slot * 0.2f, w = slot * 0.6f;
            if (n > 0)
            {
                g.FillRectangle(bar, x, bottom - h, w, h);
                var s = n.ToString();
                var sz = g.MeasureString(s, font);
                g.DrawString(s, font, fore, x + (w - sz.Width) / 2, bottom - h - sz.Height);
            }
            // 月の見出し（1 月と最初の月は年も: 26/01）
            var label = i == 0 || month.EndsWith("/01") ? month[2..] : $"{int.Parse(month[5..])}月";
            var ls = g.MeasureString(label, font);
            g.DrawString(label, font, muted, i * slot + (slot - ls.Width) / 2, bottom + 2);
        }
    }
}
