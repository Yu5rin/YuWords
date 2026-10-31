using YuWords.Core;

namespace YuWords;

/// <summary>用語を引く画面（自分が登録した語とメモ、チームの辞書の語を探す）。ダブルクリックで語句をコピー</summary>
sealed class LookupForm : Form
{
    readonly TextBox txtQuery = new() { Dock = DockStyle.Fill, PlaceholderText = "語句・よみ・意味で探す（例: ずしめ、MOP）" };
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
    readonly Label lblCount = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    List<LookupEntry> entries;

    public LookupForm(AppStore store)
    {
        Text = "用語を引く";
        Font = new Font("Yu Gothic UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Theme.Apply(this);
        Size = new Size(Dpi.Scale(640), Dpi.Scale(480));
        MinimumSize = new Size(Dpi.Scale(420), Dpi.Scale(300));
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        list.Columns.Add("語句", Dpi.Scale(150));
        list.Columns.Add("よみ", Dpi.Scale(130));
        list.Columns.Add("意味・メモ", Dpi.Scale(200));
        list.Columns.Add("", Dpi.Scale(75));
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(txtQuery);
        t.Controls.Add(list);
        t.Controls.Add(lblCount);
        Controls.Add(t);

        entries = TermLookup.Build(store);
        Fill();
        txtQuery.TextChanged += (_, _) => Fill();
        list.DoubleClick += (_, _) => CopySelected();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Down && txtQuery.Focused && list.Items.Count > 0)
            {
                list.Focus();
                list.Items[0].Selected = list.Items[0].Focused = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && list.Focused) { CopySelected(); e.Handled = true; }
        };
        // チームの辞書は共有フォルダにあるので、開いてから読む（読めなくても自分の語は引ける）
        var path = store.Settings.TeamDictionaryPath;
        if (path.Length > 0)
            Shown += async (_, _) =>
            {
                try
                {
                    var team = await Task.Run(() => TeamDictionary.Read(path));
                    if (IsDisposed) return;
                    entries = TermLookup.Build(store, team);
                    Fill();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Error("用語を引く: チームの辞書を読めませんでした", ex);
                }
            };
    }

    void Fill()
    {
        var found = TermLookup.Search(entries, txtQuery.Text);
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var e in found.Take(500))
            list.Items.Add(new ListViewItem([e.Word, e.Reading, e.Note, e.Source]) { Tag = e });
        list.EndUpdate();
        lblCount.Text = entries.Count == 0
            ? "まだ引ける語がありません。語を登録すると、ここで引けるようになります（意味は［設定］→［登録済みの語］で書けます）。"
            : $"{found.Count} 語（全 {entries.Count} 語）　ダブルクリックか Enter で語句をコピー　Esc で閉じる";
    }

    void CopySelected()
    {
        if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not LookupEntry e) return;
        try
        {
            Clipboard.SetText(e.Word);
            lblCount.Text = $"「{e.Word}」をコピーしました。";
        }
        catch (Exception) { }
    }
}
