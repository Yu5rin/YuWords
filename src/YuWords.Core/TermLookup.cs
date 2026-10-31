namespace YuWords.Core;

/// <summary>用語を引くときの1行（語句・よみ・意味と、どこにある語か）</summary>
public sealed record LookupEntry(string Word, string Reading, string Note, string Source);

/// <summary>
/// 「この略語なんだっけ」を引く小さな用語集。自分が登録した語（とそのメモ）と、チームの辞書の語を合わせて探す
/// </summary>
public static class TermLookup
{
    public const string Mine = "登録済み";
    public const string Team = "チームの辞書";

    /// <summary>引ける語の一覧。同じ語は1行にまとめ（よみは「、」でつなぐ）、自分のメモが無ければチームの辞書の意味を使う</summary>
    public static List<LookupEntry> Build(AppStore store, IEnumerable<TeamWord>? team = null)
    {
        var teamList = (team ?? []).ToList();
        var teamNotes = teamList.Where(t => t.Note.Length > 0).GroupBy(t => t.Word).ToDictionary(g => g.Key, g => g.First().Note);
        var mine = store.Registered.Where(r => r.Pos != "短縮よみ").GroupBy(r => r.Word).Select(g => new LookupEntry(
            g.Key, string.Join("、", g.Select(r => r.Reading).Distinct()),
            store.Notes.GetValueOrDefault(g.Key) ?? teamNotes.GetValueOrDefault(g.Key) ?? "", Mine)).ToList();
        var have = mine.Select(e => e.Word).ToHashSet();
        var others = teamList.Where(t => !have.Contains(t.Word)).GroupBy(t => t.Word).Select(g => new LookupEntry(
            g.Key, string.Join("、", g.Select(t => t.Reading).Distinct()), g.Select(t => t.Note).FirstOrDefault(n => n.Length > 0) ?? "", Team));
        return mine.Concat(others).OrderBy(e => e.Reading, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 探す。語句・よみ（カタカナで打ってもよい）・意味のどこかに含む語。語句の先頭に合う語を先に、次によみの先頭に合う語
    /// </summary>
    public static List<LookupEntry> Search(IEnumerable<LookupEntry> entries, string query)
    {
        var q = MailPreprocessor.Normalize(query.Trim());
        if (q.Length == 0) return entries.ToList();
        var hira = TermExtractor.KataToHira(q);
        return entries
            .Select(e => (e, rank:
                e.Word.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 :
                e.Reading.Split('、').Any(r => r.StartsWith(hira, StringComparison.Ordinal)) ? 1 :
                e.Word.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Reading.Contains(hira, StringComparison.Ordinal) ? 2 :
                e.Note.Contains(q, StringComparison.OrdinalIgnoreCase) ? 3 : -1))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank).ThenBy(x => x.e.Reading, StringComparer.Ordinal)
            .Select(x => x.e).ToList();
    }
}
