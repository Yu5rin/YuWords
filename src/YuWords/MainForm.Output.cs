using System.Diagnostics;
using YuWords.Core;

namespace YuWords;

// 確認画面の IME への登録と出力
sealed partial class MainForm
{
    // ===== IME への登録 =====

    /// <summary>IME で変換できなかった語（変換結果が分かっていて、表記と違う。定型文は除く）</summary>
    static HashSet<string> Rescued(IEnumerable<ReviewItem> items) =>
        items.Where(i => i.Category != CandidateBuilder.PhraseCategory && i.ImeConversion.Length > 0 && !i.ImeConvertible)
             .Select(i => i.Word).ToHashSet();

    async Task RegisterAsync()
    {
        grid.EndEdit();
        var items = all.Where(i => i.Register && !i.Held && i.Reading.Length > 0).ToList();
        int noReading = all.Count(i => i.Register && !i.Held && i.Reading.Length == 0);
        if (items.Count == 0) return;
        var confirm = $"登録チェックの付いた {items.Count} 語を、Microsoft IME のユーザー辞書に登録します。";
        if (noReading > 0) confirm += $"\n（よみが空欄の {noReading} 語は登録しません）";
        if (MessageBox.Show(this, confirm, "IME に登録", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        cts = new CancellationTokenSource();
        SetBusy(true);
        SetStatus($"IME に登録中... 0 / {items.Count}");
        var words = items.SelectMany(i => i.Entries()).ToList();
        var rescued = Rescued(items);   // 登録で変換が変わる前に覚えておく
        var progress = new Progress<int>(n => SetStatus($"IME に登録中... {n} / {items.Count}"));
        bool fallback = false;
        try
        {
            var token = cts.Token;
            var report = await StaTask.Run(() => ImeRegistration.Run(words, store, progress, token));
            Log.Info($"IME に登録: 登録 {report.Registered}、既存 {report.Skipped}、失敗 {report.Failed.Count}");
            var ok = report.Succeeded.ToHashSet();
            store.MarkRescued(report.Succeeded.Select(w => w.Word).Where(rescued.Contains));
            // 登録した語が、そのよみで変換できるようになったかを確かめ直す
            int converts = 0, checkedCount = 0;
            foreach (var i in items.Where(i => ok.Contains((i.Reading, i.Word, i.Pos))))
            {
                Reconvert(i);
                checkedCount++;
                if (i.ImeConvertible) converts++;
            }
            foreach (var i in items.Where(i => ok.Contains((i.Reading, i.Word, i.Pos))))
            {
                i.IsRegistered = true;
                (i.Register, i.Held) = (false, false);
            }
            grid.Invalidate();
            ScheduleSave();

            var summary = $"登録 {report.Registered} 語、既にあったもの {report.Skipped} 語、失敗 {report.Failed.Count} 語";
            if (report.LooksIneffective)
            {
                // 1語も入らなかった: 直接登録がこの PC では効かないとみなし、辞書ファイル＋ユーザー辞書ツールの流れに切り替えてもらう
                SetStatus(summary);
                Log.Error($"直接登録で1語も入りませんでした（{summary}）");
                fallback = MessageBox.Show(this,
                    $"{summary}\n\nIME への直接登録で、1語も辞書に入りませんでした" +
                    (report.NotFound > 0 ? $"（IME は登録したと返しましたが、読み直すと {report.NotFound} 語とも辞書にありませんでした）" : "") + "。\n\n" +
                    "辞書ファイルに出力して、ユーザー辞書ツールで取り込みますか？\n（この PC では直接登録が効かないようなので、［設定］の「IME のユーザー辞書を直接使う」を OFF にすることもできます）",
                    "IME に登録", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                goto Done;   // 後片付け（finally）のあとで、辞書ファイルの流れに進む
            }
            var verify = checkedCount == 0 ? "" :
                $"\n\n登録後に変換を確かめた結果: {checkedCount} 語のうち {converts} 語が、そのよみで最初の候補に出ます。" +
                (converts < checkedCount ? "\n残りは別の候補が先に出ますが、変換候補の一覧や予測変換には出ます（使ううちに上に来ます）。" : "");
            SetStatus(summary);
            var msg = token.IsCancellationRequested ? "途中で中止しました。\n" + summary : summary;
            if (report.Verified) msg += "\n（登録のあとに辞書を読み直して、入ったことを確かめました）";
            if (report.NotFound > 0) msg += $"\nIME は登録したと返しましたが、{report.NotFound} 語は辞書に入っていなかったので失敗に数えています。";
            if (report.Failed.Count > 0)
            {
                msg += "\n\n登録できなかった語:\n" + string.Join("\n",
                    report.Failed.Take(15).Select(f => $"  {f.Word}（{f.Reading}・{f.Pos}）"));
                if (report.Failed.Count > 15) msg += $"\n  ほか {report.Failed.Count - 15} 語";
                msg += "\n\n登録チェックは残してあります。よみ・品詞を見直すか、テキストに出力して取り込んでください。";
            }
            msg += verify;
            msg += store.BackupError == null
                ? $"\n\n持ち出し用の辞書も更新しました。\n{store.BackupFile}"
                : $"\n\n持ち出し用の辞書を書けませんでした（{store.BackupError}）。［設定］→［持ち出し用辞書］で保存先を見直してください。";
            MessageBox.Show(this, msg, "IME に登録", MessageBoxButtons.OK,
                report.Failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (InvalidOperationException ex)
        {
            // 新しい IME で直接登録が使えない場合など。テキスト出力に切り替えてもらう
            SetStatus("IME に直接登録できませんでした。");
            fallback = MessageBox.Show(this,
                $"IME のユーザー辞書に直接登録できませんでした。\n{ex.Message}\n\n" +
                "代わりに IME 用テキストに出力し、ユーザー辞書ツールで取り込みますか？",
                "IME に登録", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }
        catch (Exception ex)
        {
            SetStatus("IME への登録に失敗しました。");
            MessageBox.Show(this, ex.Message, "IME に登録", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            cts.Dispose();
            cts = null;
            SetBusy(false);
        }
    Done:
        if (fallback && ExportIme(offerFolder: false))
            OpenDictionaryTool();
    }

    /// <summary>
    /// 登録チェックの付いた語を Google 日本語入力に登録する。Google には外から登録する仕組みが無いので、
    /// Google 形式のファイルを書き出して辞書ツールを開き、インポートしてもらう
    /// </summary>
    void RegisterGoogle()
    {
        grid.EndEdit();
        var items = all.Where(i => i.Register && !i.Held && i.Reading.Length > 0).ToList();
        if (items.Count == 0) return;
        try
        {
            var (path, n) = GoogleIme.Export(store.Dir, items.SelectMany(i => i.Entries()));
            store.AddRegistered(items.SelectMany(i => i.Entries()).Select(e => (e.Word, e.Reading, e.Pos)), GoogleIme.Target);
            store.MarkRescued(Rescued(items));
            try { Clipboard.SetText(path); } catch (Exception) { }
            Log.Info($"Google 日本語入力用に {n} 語を書き出し");
            var error = GoogleIme.OpenDictionaryTool();
            SetStatus($"Google 日本語入力用に {n} 語を書き出しました。辞書ツールでインポートしてください。");
            MessageBox.Show(this,
                $"Google 日本語入力用に {n} 語を書き出しました。\n{path}\n（このファイルの場所をクリップボードにコピーしました）\n\n" +
                (error == null
                    ? "開いた Google 日本語入力の辞書ツールで、次のように取り込んでください。\n" +
                      "  ［管理］→［新規辞書にインポート］（または［選択した辞書にインポート］）\n" +
                      "  → ファイル名の欄に貼り付け（Ctrl+V）→［開く］\n" +
                      "  → 形式は「自動検出」のままで［インポート］"
                    : $"辞書ツールを開けませんでした（{error}）。Google 日本語入力のプロパティから辞書ツールを開いて、このファイルをインポートしてください。") +
                "\n\nMicrosoft IME にも登録する場合は、続けて「IME に登録」を押してください（登録チェックは残してあります）。",
                "Google 日本語入力に登録", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log.Error("Google 日本語入力用の書き出しに失敗", ex);
            MessageBox.Show(this, $"書き出せませんでした。\n{ex.Message}", "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void OpenDictionaryTool()
    {
        var tool = ImeDictionary.DictionaryToolPath();
        if (tool == null)
        {
            MessageBox.Show(this, "ユーザー辞書ツールが見つかりませんでした。IME のアイコンを右クリックし［単語の追加］→［ユーザー辞書ツール］から開いてください。",
                "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try { Process.Start(new ProcessStartInfo(tool) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    // ===== 出力 =====

    /// <summary>保存画面の形式の選択肢（並びは DictFormat と同じ）</summary>
    internal const string FormatFilter =
        "Microsoft IME (*.txt)|*.txt|Google 日本語入力 (*.txt)|*.txt|ATOK (*.txt)|*.txt";

    /// <summary>登録チェックの付いた語を IME 用テキストに出力する。出力したら true</summary>
    bool ExportIme(bool offerFolder = true)
    {
        grid.EndEdit();
        var items = all.Where(i => i.Register && !i.Held).ToList();
        var noReading = items.Count(i => i.Reading.Length == 0);
        items = items.Where(i => i.Reading.Length > 0).ToList();
        if (items.Count == 0)
        {
            MessageBox.Show(this, "よみのある登録チェック済みの語がありません。", "YuWords");
            return false;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "辞書のテキストに出力",
            FileName = $"yuwords_{DateTime.Now:yyyyMMdd}.txt",
            Filter = FormatFilter,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        var format = (DictFormat)(dlg.FilterIndex - 1);
        try
        {
            var path = OutputWriters.WritablePath(dlg.FileName, m => MessageBox.Show(this, m, "YuWords"));
            int n = DictionaryFormats.Write(path, items.SelectMany(i => i.Entries()), format);
            if (format != DictFormat.MicrosoftIme)
            {
                // ほかの IME 用の書き出しは、この PC の Microsoft IME への登録ではないので登録済みにはしない
                SetStatus($"{n} 語を {DictionaryFormats.Label(format)} の形式で出力しました。");
                if (MessageBox.Show(this, $"{n} 語を {DictionaryFormats.Label(format)} の辞書形式で出力しました。\n{path}\n\n" +
                        $"{DictionaryFormats.Label(format)} の辞書ツールの「インポート」などで取り込んでください。\n\n出力先のフォルダを開きますか？",
                        "出力しました", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                return true;
            }
            int added = store.AddRegistered(items.SelectMany(i => i.Entries()).Select(e => (e.Word, e.Reading, e.Pos)));
            store.MarkRescued(Rescued(items));
            foreach (var i in items) (i.IsRegistered, i.Register, i.Held) = (true, false, false);
            grid.Invalidate();
            ScheduleSave();

            var msg = $"{n} 語を出力しました。\n{path}\n\n" +
                      "Microsoft IME のユーザー辞書ツールで［ツール］→［テキスト ファイルからの登録］を選び、このファイルを取り込んでください。\n\n" +
                      (store.BackupError == null
                          ? $"登録済みリストに {added} 語を追加し、持ち出し用の辞書も更新しました。\n{store.BackupFile}"
                          : $"登録済みリストに {added} 語を追加しましたが、持ち出し用の辞書は書けませんでした（{store.BackupError}）。");
            if (noReading > 0) msg += $"\n\nよみが空欄の {noReading} 語は出力していません。";
            SetStatus($"{n} 語を IME 用テキストに出力しました。");
            if (!offerFolder)
            {
                MessageBox.Show(this, msg, "出力しました", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            msg += "\n\n出力先のフォルダを開きますか？";
            if (MessageBox.Show(this, msg, "出力しました", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"出力に失敗しました。\n{ex.Message}", "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    void ExportCsv()
    {
        grid.EndEdit();
        using var dlg = new SaveFileDialog
        {
            Title = "CSV に出力（全候補）",
            FileName = "candidates.csv",
            Filter = "CSV (*.csv)|*.csv",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var path = OutputWriters.WritablePath(dlg.FileName, m => MessageBox.Show(this, m, "YuWords"));
            OutputWriters.WriteCsv(path, all);
            SetStatus($"{all.Count} 語を CSV に出力しました: {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"出力に失敗しました。\n{ex.Message}", "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
