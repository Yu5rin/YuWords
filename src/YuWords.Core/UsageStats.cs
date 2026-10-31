namespace YuWords.Core;

/// <summary>これまでの効果（登録した語の数・推移・よく使う語）。画面に出すだけで、外には送らない</summary>
public sealed record UsageStats(
    int Total, int MicrosoftIme, int Google, int Rescued,
    IReadOnlyList<(string Month, int Count)> ByMonth,
    IReadOnlyList<(string Pos, int Count)> ByPos,
    IReadOnlyList<(string Word, int Mails)> TopUsed, int RecentMails, DateTime? RecentAt, int UsedInRecent,
    int Stale, DateTime? FirstRegistered)
{
    /// <summary>集計する。月ごとの推移は now を含む直近 months か月（登録の無い月も 0 として入れる）</summary>
    public static UsageStats Compute(AppStore store, DateTime now, int months = 12, int top = 15)
    {
        // 同じ語をよみ違い・登録先違いで何行も持つことがあるので、語の数は語句で数える
        var reg = store.Registered;
        var words = reg.GroupBy(r => r.Word).Select(g => g.OrderBy(r => r.RegisteredAt).First()).ToList();
        var start = new DateTime(now.Year, now.Month, 1).AddMonths(-(months - 1));
        var byMonth = Enumerable.Range(0, months).Select(k => start.AddMonths(k))
            .Select(m => (m.ToString("yyyy/MM"), words.Count(w => w.RegisteredAt.Year == m.Year && w.RegisteredAt.Month == m.Month)))
            .ToList();
        var byPos = words.GroupBy(w => w.Pos).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ToList();
        var registeredWords = words.Select(w => w.Word).ToHashSet();
        var recent = store.RecentUse;
        var topUsed = recent.Counts.Where(kv => registeredWords.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(top).Select(kv => (kv.Key, kv.Value)).ToList();
        var stale = WordUsage.Stale(words, store.LastUsed, store.Settings.LastUsageScan, store.Settings.StaleDays).Count;
        return new UsageStats(
            words.Count,
            reg.Where(r => r.Target == "MSIME").Select(r => r.Word).Distinct().Count(),
            reg.Where(r => r.Target == GoogleIme.Target).Select(r => r.Word).Distinct().Count(),
            reg.Where(r => r.Rescued).Select(r => r.Word).Distinct().Count(),
            byMonth, byPos, topUsed, recent.Documents, recent.At,
            recent.Counts.Keys.Count(registeredWords.Contains),
            stale,
            words.Count > 0 ? words.Min(w => w.RegisteredAt) : null);
    }
}
