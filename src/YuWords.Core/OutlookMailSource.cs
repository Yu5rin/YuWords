using System.Runtime.InteropServices;

namespace YuWords.Core;

/// <summary>Outlook クラシック版の「送信済みアイテム」を COM（遅延バインディング）で読む</summary>
public static class OutlookMailSource
{
    const int OlFolderSentMail = 5;
    const int OlMail = 43;

    /// <summary>
    /// 送信日時の新しい順に読み、各メールを「件名＋引用除去済み本文の行」にして返す。
    /// 読めないアイテムは飛ばす。
    /// </summary>
    /// <param name="since">指定すると、この日時より後に送ったメールだけを読む（前回以降の差分）</param>
    public static List<List<string>> FetchSentMails(int days, int maxMails,
        IProgress<int>? progress = null, CancellationToken ct = default, DateTime? since = null)
    {
        var type = Type.GetTypeFromProgID("Outlook.Application")
                   ?? throw new InvalidOperationException(
                       "Outlook（クラシック版）が見つかりません。新しい Outlook は対象外です。");
        dynamic app;
        try
        {
            app = Activator.CreateInstance(type)!;
        }
        catch (COMException e)
        {
            throw new InvalidOperationException($"Outlook に接続できませんでした（{e.Message}）。", e);
        }

        dynamic items = app.GetNamespace("MAPI").GetDefaultFolder(OlFolderSentMail).Items;
        var cutoff = DateTime.Now.Date.AddDays(-days);
        // 対象期間のメールだけに絞ってから読む（送信済みアイテムが多いときに速い）。
        // 日付の書式は Windows の地域設定に合わせる必要がある。絞れなければ全件を新しい順にたどる
        var from = since is { } s && s > cutoff ? s : cutoff;
        try
        {
            items = items.Restrict($"[SentOn] >= '{from.ToString("g", System.Globalization.CultureInfo.CurrentCulture)}'");
        }
        catch (Exception e)
        {
            Log.Error("送信済みアイテムを日付で絞れませんでした（全件をたどります）", e);
        }
        items.Sort("[SentOn]", true);
        var mails = new List<List<string>>();

        foreach (dynamic item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (mails.Count >= maxMails) break;
            try
            {
                if ((int)item.Class != OlMail) continue;
                DateTime sent = item.SentOn;
                if (sent.Date < cutoff || sent <= since) break;
                var lines = new List<string> { (string?)item.Subject ?? "" };
                lines.AddRange(MailPreprocessor.StripQuote((string?)item.Body ?? ""));
                mails.Add(lines);
                if (mails.Count % 50 == 0) progress?.Report(mails.Count);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // 読めないアイテム（権限・破損など）は飛ばす
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        progress?.Report(mails.Count);
        return mails;
    }
}
