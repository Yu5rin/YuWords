using System.Diagnostics;

namespace YuWords.Core;

/// <summary>自動チェックの頻度</summary>
public enum AutoCheckFrequency { Off, Weekly, Monthly }

/// <summary>
/// 新しいメールの差分を定期的に調べる（Windows のタスク スケジューラに登録する）。
/// 調べるだけで、前回読み込んだ日時は進めない（通知から開いたときに、同じ差分を読み込めるように）
/// </summary>
public static class AutoCheck
{
    public const string TaskName = "YuWords 新しい候補のチェック";
    public const string CheckArgument = "--check-new";

    /// <summary>schtasks に渡す登録の引数</summary>
    public static string CreateArguments(string exePath, AutoCheckFrequency frequency, int hour)
    {
        var schedule = frequency switch
        {
            AutoCheckFrequency.Weekly => "/SC WEEKLY /D MON",
            AutoCheckFrequency.Monthly => "/SC MONTHLY /D 1",
            _ => throw new ArgumentException("登録しない設定です", nameof(frequency)),
        };
        // /TR の中のパスは \" で囲む（空白や日本語のフォルダでも動くように）
        return $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" {CheckArgument}\" {schedule} /ST {Math.Clamp(hour, 0, 23):00}:00 /F";
    }

    public static string DeleteArguments() => $"/Delete /TN \"{TaskName}\" /F";

    /// <summary>タスク スケジューラに登録する（Off なら消す）。失敗したら理由を返す、成功なら null</summary>
    public static string? Apply(string exePath, AutoCheckFrequency frequency, int hour)
    {
        if (frequency == AutoCheckFrequency.Off)
        {
            if (!Exists()) return null;
            return Run(DeleteArguments());
        }
        return Run(CreateArguments(exePath, frequency, hour));
    }

    public static bool Exists() => Run($"/Query /TN \"{TaskName}\"") == null;

    static string? Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return p.ExitCode == 0 ? null : (err.Trim().Length > 0 ? err.Trim() : $"終了コード {p.ExitCode}");
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// 前回 Outlook から読み込んだあとの新しいメールから、まだ登録・除外・保留していない候補を探す。
    /// IME の COM を使うので STA スレッドで呼ぶこと。前回の日時が無ければ null
    /// </summary>
    public static (int Mails, List<Candidate> NewWords)? FindNewWords(AppStore store, CancellationToken ct = default)
    {
        if (store.Settings.LastOutlookRead is not { } since) return null;
        var s = store.Settings.Extractor;
        var mails = OutlookMailSource.FetchSentMails(s.Days, s.MaxMails, null, ct, since);
        if (mails.Count == 0) return (0, []);
        var rows = Extraction.Run(mails, s, new HashSet<string>(store.Excluded), null, ct, null, store.Learned);
        var ime = store.Settings.CheckImeDictionary ? ImeRegistration.ReadUserDictionary() : null;
        var fresh = rows.Where(c => c.Category != CandidateBuilder.PhraseCategory
                                    && !store.Held.ContainsKey(c.Word)
                                    && !store.IsRegistered(c.Word, c.Reading)
                                    && ime?.Contains((c.Word, c.Reading)) != true).ToList();
        return (mails.Count, fresh);
    }
}
