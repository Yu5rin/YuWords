using YuWords.Core;

namespace YuWords;

// 新しい PC への引っ越し（設定・除外リスト・登録済みの語・直したよみをまとめて書き出す・読み込む）
sealed partial class MainForm
{
    /// <summary>引っ越しファイルを読み込んで起動し直すところ。閉じるときに今の設定で上書きしない</summary>
    bool restartingAfterImport;

    public void ExportMigration()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "引っ越し用にまとめて書き出す",
            FileName = $"YuWords_引っ越し_{DateTime.Now:yyyyMMdd}.zip",
            Filter = "YuWords の引っ越しファイル (*.zip)|*.zip",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SaveLayout();
            int n = Migration.Export(store, dlg.FileName);
            Log.Info($"引っ越しファイルを書き出しました（{n} ファイル）");
            MessageBox.Show(this,
                $"設定・除外リスト・登録済みの語（{store.Registered.Count} 語）・直したよみなどを書き出しました。\n{dlg.FileName}\n\n" +
                "新しい PC で YuWords を起動し、「使い方」→「前の PC から引っ越す...」でこのファイルを選んでください。\n" +
                "（途中の一覧とメールの例文は入れていません）",
                "引っ越し", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"書き出せませんでした。\n{ex.Message}", "引っ越し", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>引っ越しファイルを選んで読み込む</summary>
    void ImportMigration()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "前の PC で書き出した引っ越しファイル",
            Filter = "YuWords の引っ越しファイル (*.zip)|*.zip",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) ImportMigration(dlg.FileName);
    }

    /// <summary>引っ越しファイルを読み込み、YuWords を起動し直す。起動し直したあと、登録済みの語を IME に登録するか尋ねる</summary>
    public void ImportMigration(string path)
    {
        Migration.Summary s;
        try { s = Migration.Inspect(path); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"引っ越しファイルを読めませんでした。\n{ex.Message}", "引っ越し", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var replace = store.Registered.Count > 0 || store.Excluded.Count > 0
            ? $"\n\nこの PC の今の設定・登録済みの語（{store.Registered.Count} 語）は置き換わります（置き換える前のものは設定フォルダに控えます）。"
            : "";
        if (MessageBox.Show(this,
                $"前の PC（YuWords {s.FromVersion}、{s.CreatedAt} に書き出し）から引っ越します。\n\n" +
                $"・登録済みの語: {s.Registered} 語\n・除外リスト: {s.Excluded} 語\n・直したよみ: {s.Learned} 語\n・よみのルール: {s.Rules} 件\n・保留にした語: {s.Held} 語" +
                replace + "\n\n読み込んだあと YuWords を起動し直します。",
                "引っ越し", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        try
        {
            var backup = Migration.Import(path, store.Dir);
            Log.Info($"引っ越しファイルを読み込みました（前のものは {backup}）");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"読み込めませんでした。\n{ex.Message}", "引っ越し", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        // 起動し直したあとに、登録済みの語を IME に入れるか尋ねる（新しい PC の IME は空のため）
        File.WriteAllText(PendingRestorePath, "");
        restartingAfterImport = true;
        try { ReviewSession.Delete(store.Dir); } catch (Exception) { }
        Application.Restart();
    }

    string PendingRestorePath => Path.Combine(store.Dir, "migration_pending_ime");

    /// <summary>引っ越しのあとの起動なら、登録済みの語を IME に登録するか尋ねる</summary>
    void OfferImeRestoreAfterMigration()
    {
        if (!File.Exists(PendingRestorePath)) return;
        Shown += async (_, _) =>
        {
            try { File.Delete(PendingRestorePath); } catch (Exception) { }
            store.RefreshBackup();
            var words = store.Registered.Where(r => r.Target == "MSIME").Select(r => (r.Reading, r.Word, r.Pos)).ToList();
            if (words.Count == 0)
            {
                SetStatus("前の PC から引っ越しました。");
                return;
            }
            if (MessageBox.Show(this,
                    $"前の PC から引っ越しました。\n\n前の PC で登録した {words.Count} 語を、この PC の IME のユーザー辞書に登録しますか？",
                    "引っ越し", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                SetStatus("前の PC から引っ越しました。IME への登録は［設定］→［持ち出し用辞書］からもできます。");
                return;
            }
            Enabled = false;
            UseWaitCursor = true;
            try
            {
                var r = await StaTask.Run(() => ImeRegistration.Run(words, store));
                RefreshRegistered();
                Refilter();
                MessageBox.Show(this, $"登録 {r.Registered} 語、既にあったもの {r.Skipped} 語、失敗 {r.Failed.Count} 語",
                    "引っ越し", MessageBoxButtons.OK, r.Failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this,
                    $"IME に直接登録できませんでした。\n{ex.Message}\n\n" +
                    $"持ち出し用辞書（{store.BackupFile}）を Microsoft IME のユーザー辞書ツールの［ツール］→［テキスト ファイルからの登録］で取り込んでください。",
                    "引っ越し", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
                Enabled = true;
            }
        };
    }
}
