using YuWords.Core;

namespace YuWords;

/// <summary>持ち出し用辞書（IME 取り込み形式のテキスト）を選んで IME に登録する。新しい PC での復元用</summary>
static class RestoreHelper
{
    /// <summary>登録まで進んだら true</summary>
    public static bool FromFile(IWin32Window owner, AppStore store, string? initialPath = null)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "IME に登録するファイル（前の PC の dictionary_backup.txt など）",
            Filter = "辞書のテキスト・CSV（Microsoft IME・Google 日本語入力・ATOK）(*.txt;*.csv)|*.txt;*.csv|すべてのファイル (*.*)|*.*",
            FileName = initialPath != null && File.Exists(initialPath) ? initialPath : "",
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
        List<(string Reading, string Word, string Pos)> words;
        try { words = DictionaryFormats.Read(dlg.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"読み込めませんでした。\n{ex.Message}", "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        if (words.Count == 0)
        {
            MessageBox.Show(owner, "登録できる語がありませんでした。", "YuWords");
            return false;
        }
        if (MessageBox.Show(owner, $"{words.Count} 語を IME のユーザー辞書に登録します。", "IME に登録",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return false;

        var cursor = Cursor.Current;
        try
        {
            Cursor.Current = Cursors.WaitCursor;
            var r = ImeRegistration.Run(words, store);
            Cursor.Current = cursor;
            MessageBox.Show(owner, $"登録 {r.Registered} 語、既にあったもの {r.Skipped} 語、失敗 {r.Failed.Count} 語",
                "IME に登録", MessageBoxButtons.OK, r.Failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            Cursor.Current = cursor;
            MessageBox.Show(owner,
                $"IME に直接登録できませんでした。\n{ex.Message}\n\nこのファイルは Microsoft IME のユーザー辞書ツールの［ツール］→［テキスト ファイルからの登録］で取り込めます。",
                "IME に登録", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    /// <summary>初めて起動したときの案内。前の PC から移ってきた人には、持ち出し用辞書の登録を勧める</summary>
    public static void Welcome(IWin32Window owner, AppStore store)
    {
        store.Settings.Welcomed = true;
        store.SaveSettings();
        var ans = MessageBox.Show(owner,
            "YuWords へようこそ。\n\n" +
            "前の PC で YuWords を使っていて、持ち出し用辞書（dictionary_backup.txt）がありますか？\n\n" +
            "［はい］そのファイルを選んで、IME にまとめて登録します。\n" +
            "［いいえ］「Outlook から読み込む」などで、自分の文章から語を集めるところから始めます。",
            "YuWords", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ans == DialogResult.Yes) FromFile(owner, store);
    }
}
