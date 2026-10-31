using System.Diagnostics;
using YuWords.Core;

namespace YuWords;

/// <summary>
/// タスク スケジューラから「YuWords.exe --check-new」で起動されたときの処理。
/// 画面は出さずに新しいメールの差分を調べ、新しい候補があれば通知を出す。通知をクリックすると YuWords を開いて読み込む
/// </summary>
sealed class CheckNewRunner : ApplicationContext
{
    readonly NotifyIcon icon = new() { Icon = SystemIcons.Information, Text = "YuWords" };
    readonly System.Windows.Forms.Timer timeout = new() { Interval = 60_000 };

    public CheckNewRunner(AppStore store)
    {
        timeout.Tick += (_, _) => ExitThread();
        icon.BalloonTipClicked += (_, _) => OpenApp();
        icon.Click += (_, _) => OpenApp();
        icon.BalloonTipClosed += (_, _) => ExitThread();
        _ = RunAsync(store);
    }

    async Task RunAsync(AppStore store)
    {
        try
        {
            var result = await StaTask.Run(() => AutoCheck.FindNewWords(store));
            if (result is not { } r || r.NewWords.Count == 0)
            {
                Log.Info($"自動チェック: 新しい候補なし（メール {result?.Mails ?? 0} 件）");
                ExitThread();
                return;
            }
            Log.Info($"自動チェック: メール {r.Mails} 件から新しい候補 {r.NewWords.Count} 語");
            var top = string.Join("、", r.NewWords.OrderByDescending(c => c.MailCount).Take(5).Select(c => c.Word));
            icon.Visible = true;
            icon.ShowBalloonTip(30_000, $"YuWords: 新しい候補が {r.NewWords.Count} 語あります",
                $"{top} など。クリックすると開いて確認できます。", ToolTipIcon.Info);
            timeout.Start();
        }
        catch (Exception e)
        {
            Log.Error("自動チェックに失敗", e);
            ExitThread();
        }
    }

    void OpenApp()
    {
        try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, YuWords.MainForm.NewOnlyArgument) { UseShellExecute = true }); }
        catch (Exception e) { Log.Error("YuWords を開けませんでした", e); }
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        icon.Visible = false;
        icon.Dispose();
        timeout.Dispose();
        base.ExitThreadCore();
    }
}
