using YuWords.Core;

namespace YuWords;

/// <summary>持ち出し用辞書（IME 取り込み形式のテキスト）を選んで IME に登録する。新しい PC での復元用</summary>
static class RestoreHelper
{
    /// <summary>登録まで進んだら true。登録は別のスレッドで行い、その間は owner を操作できないようにする（画面は固めない）</summary>
    public static async Task<bool> FromFileAsync(Form owner, AppStore store, string? initialPath = null)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "IME に登録するファイル（前の PC の dictionary_backup.txt など）",
            Filter = "辞書のテキスト・CSV・引っ越しファイル (*.txt;*.csv;*.zip)|*.txt;*.csv;*.zip|すべてのファイル (*.*)|*.*",
            FileName = initialPath != null && File.Exists(initialPath) ? initialPath : "",
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
        if (Migration.IsMigrationFile(dlg.FileName))
        {
            // 引っ越しファイル（設定ごと）なら、引っ越しとして読み込む
            if (owner is MainForm main) main.ImportMigration(dlg.FileName);
            else MessageBox.Show(owner, "これは引っ越しファイルです。設定画面を閉じて、「使い方」→「前の PC から引っ越す...」で選んでください。", "YuWords");
            return false;
        }
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

        owner.UseWaitCursor = true;
        owner.Enabled = false;
        try
        {
            var r = await StaTask.Run(() => ImeRegistration.Run(words, store));
            owner.Enabled = true;
            owner.UseWaitCursor = false;
            MessageBox.Show(owner, $"登録 {r.Registered} 語、既にあったもの {r.Skipped} 語、失敗 {r.Failed.Count} 語",
                "IME に登録", MessageBoxButtons.OK, r.Failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            owner.Enabled = true;
            owner.UseWaitCursor = false;
            MessageBox.Show(owner,
                $"IME に直接登録できませんでした。\n{ex.Message}\n\nこのファイルは Microsoft IME のユーザー辞書ツールの［ツール］→［テキスト ファイルからの登録］で取り込めます。",
                "IME に登録", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        finally
        {
            owner.Enabled = true;
            owner.UseWaitCursor = false;
        }
    }

    /// <summary>初めて起動したときの案内。前の PC から移ってきた人には、持ち出し用辞書の登録を勧める</summary>
    public static async Task Welcome(Form owner, AppStore store)
    {
        store.Settings.Welcomed = true;
        store.SaveSettings();
        var ans = MessageBox.Show(owner,
            "YuWords へようこそ。\n\n" +
            "前の PC で YuWords を使っていて、引っ越しファイル（YuWords_引っ越し_….zip）か持ち出し用辞書（dictionary_backup.txt）がありますか？\n\n" +
            "［はい］そのファイルを選びます。引っ越しファイルなら設定ごと、持ち出し用辞書なら登録した語を IME にまとめて登録します。\n" +
            "［いいえ］「Outlook から読み込む」などで、自分の文章から語を集めるところから始めます。",
            "YuWords", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ans == DialogResult.Yes) await FromFileAsync(owner, store);
    }
}
