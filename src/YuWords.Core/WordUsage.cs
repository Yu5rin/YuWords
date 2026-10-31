namespace YuWords.Core;

/// <summary>
/// 登録済みの語が、読み込んだ文章にまだ出てくるかを調べる（「使わなくなったかもしれない語」の判定に使う）。
/// 抽出の候補は出現数で絞られるので、文章をそのまま探す
/// </summary>
public static class WordUsage
{
    /// <summary>文書の中に出てきた語（words のうち）。語は表記の正規化（全角英数→半角など）をそろえて探す</summary>
    public static HashSet<string> Find(IEnumerable<IEnumerable<string>> docs, IEnumerable<string> words, CancellationToken ct = default)
    {
        // 先頭の1文字ごとに語をまとめ、文章を1回なめるだけで探す（語が多くても速い）
        var byFirst = new Dictionary<char, List<(string Original, string Key)>>();
        foreach (var w in words.Distinct())
        {
            var key = MailPreprocessor.Normalize(w);
            if (key.Length == 0) continue;
            if (!byFirst.TryGetValue(key[0], out var list)) byFirst[key[0]] = list = [];
            list.Add((w, key));
        }
        var found = new HashSet<string>();
        if (byFirst.Count == 0) return found;
        int remaining = byFirst.Values.Sum(l => l.Count);
        foreach (var doc in docs)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var line in doc)
            {
                var text = MailPreprocessor.Normalize(line).AsSpan();
                for (int i = 0; i < text.Length; i++)
                {
                    if (!byFirst.TryGetValue(text[i], out var list)) continue;
                    var rest = text[i..];
                    for (int k = list.Count - 1; k >= 0; k--)
                    {
                        if (!rest.StartsWith(list[k].Key, StringComparison.Ordinal)) continue;
                        found.Add(list[k].Original);
                        list.RemoveAt(k);   // 見つかった語はもう探さない
                        remaining--;
                    }
                    if (list.Count == 0) byFirst.Remove(text[i]);
                    if (remaining == 0) return found;
                }
            }
        }
        return found;
    }

    /// <summary>
    /// 使わなくなったかもしれない語。最後に文章で見かけた日（見かけていなければ登録した日）から、
    /// 文章を調べ続けて days 日以上たっても出てこなかった語。まだ文章を調べていなければ空
    /// </summary>
    public static List<RegisteredWord> Stale(IEnumerable<RegisteredWord> registered, IReadOnlyDictionary<string, DateTime> lastUsed,
        DateTime? lastScan, int days)
    {
        if (lastScan is not { } scan || days <= 0) return [];
        return registered.Where(r => r.Pos != "短縮よみ" && scan - LastSeen(r, lastUsed) >= TimeSpan.FromDays(days)).ToList();
    }

    /// <summary>最後に文章で見かけた日。見かけていなければ登録した日</summary>
    public static DateTime LastSeen(RegisteredWord r, IReadOnlyDictionary<string, DateTime> lastUsed) =>
        lastUsed.TryGetValue(r.Word, out var t) && t > r.RegisteredAt ? t : r.RegisteredAt;
}
