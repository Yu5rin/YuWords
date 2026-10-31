using System.Diagnostics;
using YuWords.Core;

namespace YuWords;

// 確認画面の自動更新（新しい版を探す・取ってくる・差し替えて起動し直す）
sealed partial class MainForm
{
    /// <summary>起動したときに、前回から一定の時間がたっていれば新しい版を探す（見つからなければ何も出さない）</summary>
    void CheckForUpdatesOnStartup()
    {
        var s = store.Settings;
        if (!s.UpdateCheck) return;
        if (s.LastUpdateCheck is { } last && DateTime.Now - last < TimeSpan.FromHours(20)) return;
        Shown += async (_, _) =>
        {
            await Task.Delay(1500);   // 起動の邪魔をしない
            await CheckForUpdatesAsync(manual: false);
        };
    }

    /// <summary>新しい版を探す。manual なら、見つからなかったときや失敗したときも知らせる</summary>
    async Task CheckForUpdatesAsync(bool manual)
    {
        if (cts != null) return;
        var s = store.Settings;
        UpdateInfo? info;
        try
        {
            if (manual) SetStatus("新しい版を確かめています...");
            info = s.UpdateSource == "folder" && s.UpdateFolder.Length > 0
                ? await Task.Run(() => Updater.CheckFolder(s.UpdateFolder, Updater.CurrentVersion))
                : await Updater.CheckGitHubAsync(Updater.CurrentVersion);
            s.LastUpdateCheck = DateTime.Now;
            store.SaveSettings();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Log.Error("新しい版を確かめられませんでした", ex);
            if (manual)
            {
                SetStatus("新しい版を確かめられませんでした。");
                MessageBox.Show(this,
                    $"新しい版を確かめられませんでした。\n{ex.Message}\n\n" +
                    "会社のネットワークで GitHub につながらないときは、［設定］→［更新］で確認先を社内の共有フォルダにするか、" +
                    "「使い方」→「ファイルから更新...」で配布物の zip を選んでください。",
                    "更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return;
        }

        if (info == null)
        {
            if (manual)
            {
                SetStatus($"最新の版です（v{Updater.CurrentVersion}）。");
                MessageBox.Show(this, $"最新の版です（v{Updater.CurrentVersion}）。", "更新");
            }
            return;
        }
        if (!manual && s.SkippedVersion == info.Version.ToString()) return;
        Log.Info($"新しい版 v{info.Version} が見つかりました");
        AskToUpdate(info);
    }

    void AskToUpdate(UpdateInfo info)
    {
        var update = new TaskDialogButton("今すぐ更新");
        var later = new TaskDialogButton("あとで");
        var skip = new TaskDialogButton("この版は知らせない");
        var page = new TaskDialogPage
        {
            Caption = "YuWords の更新",
            Heading = $"新しい版 v{info.Version} があります（今は v{Updater.CurrentVersion}）",
            Text = info.Notes.Length > 0
                ? "変更点:\n" + (info.Notes.Length > 1200 ? info.Notes[..1200] + "\n…" : info.Notes)
                : "更新すると YuWords を閉じて新しい版に差し替え、起動し直します。",
            Icon = TaskDialogIcon.Information,
            Buttons = { update, later, skip },
            DefaultButton = update,
            Footnote = "途中の一覧と選択は保存されるので、更新後も続きから再開できます。",
        };
        var result = TaskDialog.ShowDialog(this, page);
        if (result == skip)
        {
            store.Settings.SkippedVersion = info.Version.ToString();
            store.SaveSettings();
        }
        else if (result == update)
            _ = ApplyUpdateAsync(info);
    }

    /// <summary>手元の配布物（YuWords-x.y.z-win-x64.zip）を選んで更新する（会社のネットワークで GitHub につながらないとき）</summary>
    async Task UpdateFromFileAsync()
    {
        if (cts != null) return;
        using var dlg = new OpenFileDialog
        {
            Title = "更新に使う配布物を選ぶ（YuWords-x.y.z-win-x64.zip）",
            Filter = "YuWords の配布物 (YuWords-*-win-x64.zip)|YuWords-*-win-x64.zip|zip (*.zip)|*.zip",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var info = Updater.FromFile(dlg.FileName);
        if (info.Version != new Version(0, 0, 0) && info.Version <= Updater.CurrentVersion &&
            MessageBox.Show(this, $"選んだ配布物は v{info.Version} で、今の版（v{Updater.CurrentVersion}）より新しくありません。それでも入れ替えますか？",
                "更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        await ApplyUpdateAsync(info);
    }

    /// <summary>配布物を取ってきて展開し、YuWords を閉じて差し替える</summary>
    async Task ApplyUpdateAsync(UpdateInfo info)
    {
        if (cts != null) return;
        var appDir = AppContext.BaseDirectory.TrimEnd('\\');
        if (Updater.IsDevelopmentBuild(appDir))
        {
            MessageBox.Show(this, $"開発中の実行（{appDir}）なので、差し替えはしません。配布物の版で更新してください。", "更新");
            return;
        }
        if (!Updater.CanWrite(appDir))
        {
            MessageBox.Show(this,
                $"YuWords のフォルダに書き込めないので、更新できません。\n{appDir}\n\n" +
                "Program Files などではなく、ドキュメントやデスクトップなど自分のフォルダに YuWords のフォルダごと移してから更新してください。",
                "更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        cts = new CancellationTokenSource();
        SetBusy(true);
        var progress = new Progress<(long Done, long Total)>(p =>
            SetStatus(p.Total > 0 ? $"v{info.Version} を取ってきています... {p.Done * 100 / p.Total}%（{p.Done / 1048576} / {p.Total / 1048576} MB）"
                                  : $"v{info.Version} を取ってきています... {p.Done / 1048576} MB"));
        string staged;
        try
        {
            SetStatus($"v{info.Version} を取ってきています...");
            var zip = await Updater.DownloadAsync(info, progress, cts.Token);
            SetStatus("展開しています...");
            staged = await Task.Run(() => Updater.Extract(zip, info.Version));
            if (info.Sha256 == null) Log.Info("SHA256 が分からない配布物で更新します（共有フォルダ・手元のファイル）");
        }
        catch (OperationCanceledException)
        {
            SetStatus("更新を中止しました。");
            return;
        }
        catch (Exception ex)
        {
            Log.Error("更新の準備に失敗", ex);
            SetStatus("更新できませんでした。");
            MessageBox.Show(this, $"更新できませんでした。\n{ex.Message}", "更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            cts?.Dispose();
            cts = null;
            SetBusy(false);
        }

        if (MessageBox.Show(this, $"v{info.Version} の準備ができました。YuWords を閉じて差し替え、起動し直します。",
                "更新", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            return;
        try
        {
            var log = Path.Combine(Updater.UpdateDir, "apply.log");
            var script = Updater.WriteApplyScript(staged, appDir, Environment.ProcessId, log);
            Log.Info($"v{info.Version} に差し替えます（{script}）");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\"\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Exception ex)
        {
            Log.Error("差し替えを始められませんでした", ex);
            MessageBox.Show(this,
                $"差し替えを始められませんでした。\n{ex.Message}\n\n手で更新する場合は、次のフォルダの中身を YuWords のフォルダに上書きしてください。\n{staged}",
                "更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Close();   // 途中の一覧は閉じるときに保存される
    }

    /// <summary>更新のあとに起動し直したときの知らせ</summary>
    public void ShowUpdateResult(string arg)
    {
        Shown += (_, _) =>
        {
            if (arg == "--updated")
            {
                Log.Info($"v{Updater.CurrentVersion} に更新しました");
                SetStatus($"v{Updater.CurrentVersion} に更新しました。");
                try { Directory.Delete(Updater.UpdateDir, true); } catch (Exception) { }
            }
            else
            {
                Log.Error("差し替えに失敗しました（apply.log を見てください）");
                MessageBox.Show(this,
                    $"更新の差し替えに失敗しました（ファイルが使用中だった可能性があります）。もう一度「使い方」→「更新を確認...」を試してください。\n\n記録: {Path.Combine(Updater.UpdateDir, "apply.log")}",
                    "更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
    }
}
