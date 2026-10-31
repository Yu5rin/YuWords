namespace YuWords.Core;

/// <summary>辞書登録の候補1つ分</summary>
public sealed class Candidate
{
    public required string Word { get; init; }
    public required string Reading { get; set; }
    /// <summary>出現メール数</summary>
    public int MailCount { get; init; }
    public required string Category { get; init; }
    /// <summary>署名・定型行から抽出された語か</summary>
    public bool FromSignature { get; init; }
    public bool Unsure { get; set; }
    /// <summary>IME に表記を渡したときのよみ（参考。形態素解析の推測と食い違えば両方を見せる）</summary>
    public string ImeReading { get; set; } = "";
    /// <summary>IME にこのよみを渡したときの変換結果（参考）</summary>
    public string ImeConversion { get; set; } = "";
    public bool ImeConvertible => ImeConversion == Word;
    /// <summary>その語を含む本文の1行（よみ判断の手がかり）</summary>
    public string Example { get; init; } = "";
    /// <summary>その語を含む本文の行（違うものを最大 5 行）</summary>
    public List<string> Examples { get; init; } = [];
    /// <summary>よみの出どころ。空なら形態素解析の推測、「連絡先」なら Outlook の連絡先のふりがな</summary>
    public string ReadingSource { get; set; } = "";
    /// <summary>長い語の短いよみの提案（ひほか など）。提案できなければ空</summary>
    public string ShortReading { get; set; } = "";
}

/// <summary>メール群から候補を集計する（参照実装の main の集計部分）</summary>
public static class CandidateBuilder
{
    sealed class Info
    {
        public required string Reading;
        public required bool Unsure;
        public required string Category;
        public required string Example;
        public string ShortReading = "";
        public readonly List<string> Examples = [];

        public void AddExample(string line)
        {
            if (Examples.Count < MaxExamples && !Examples.Contains(line)) Examples.Add(line);
        }
    }

    /// <param name="mails">各メールの行（件名＋引用除去済み本文）</param>
    public static List<Candidate> Build(IReadOnlyList<List<string>> mails, TermExtractor extractor,
        ExtractorSettings settings, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var (bodies, common) = MailPreprocessor.SplitCommonLines(mails, settings.CommonLineRatio);
        progress?.Report($"署名・定型文として {common.Count} 行を分離");

        var count = new Dictionary<string, int>();
        var order = new List<string>();   // 同数のときは最初に出た順（参照実装の Counter と同じ）
        var info = new Dictionary<string, Info>();
        var sigWords = new HashSet<string>();
        // 部分語（長い複合語の一部）: 単独または部分として出たメール数と、含まれていた複合語の種類
        var anyCount = new Dictionary<string, int>();
        var subParents = new Dictionary<string, HashSet<string>>();
        var subInfo = new Dictionary<string, Info>();

        void Touch(string w)
        {
            if (!count.ContainsKey(w)) { count[w] = 0; order.Add(w); }
        }

        for (int i = 0; i < bodies.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var seen = new HashSet<string>();
            var seenAny = new HashSet<string>();
            foreach (var (t, line) in extractor.Extract(bodies[i]))
            {
                if (t.IsCommon) continue;
                seenAny.Add(t.Word);
                if (t.Parent != null)
                {
                    if (!subParents.TryGetValue(t.Word, out var ps)) subParents[t.Word] = ps = [];
                    ps.Add(t.Parent);
                    subInfo.TryAdd(t.Word, new Info { Reading = t.Reading, Unsure = t.Unsure, Category = t.Category, Example = line, ShortReading = t.ShortReading });
                    subInfo[t.Word].AddExample(line);
                    continue;
                }
                seen.Add(t.Word);
                if (info.TryGetValue(t.Word, out var d))
                    d.Unsure |= t.Unsure;
                else
                    info[t.Word] = new Info { Reading = t.Reading, Unsure = t.Unsure, Category = t.Category, Example = line, ShortReading = t.ShortReading };
                info[t.Word].AddExample(line);
                Touch(t.Word);
            }
            foreach (var w in seen) count[w]++;
            foreach (var w in seenAny) anyCount[w] = anyCount.GetValueOrDefault(w) + 1;
            if ((i + 1) % 100 == 0) progress?.Report($"  解析 {i + 1}/{bodies.Count}");
        }

        foreach (var (line, df) in common)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var (t, normalized) in extractor.Extract(line))
            {
                if (t.IsCommon || t.Parent != null) continue;
                info.TryAdd(t.Word, new Info { Reading = t.Reading, Unsure = t.Unsure, Category = t.Category, Example = normalized, ShortReading = t.ShortReading });
                info[t.Word].AddExample(normalized);
                Touch(t.Word);
                count[t.Word] = Math.Max(count[t.Word], df);
                sigWords.Add(t.Word);
            }
        }

        // 2種類以上の複合語に共通する部分は、それだけでも使う語の可能性が高い（「標準コアユニット」月次報告／仕様書）
        foreach (var (w, parents) in subParents)
        {
            if (parents.Count < 2 || count.GetValueOrDefault(w) >= settings.MinMails) continue;
            Touch(w);
            count[w] = anyCount[w];
            info.TryAdd(w, subInfo[w]);
        }

        var rows = order
            .Select((w, idx) => (w, idx))
            .Where(x => count[x.w] >= settings.MinMails)
            .OrderByDescending(x => count[x.w]).ThenBy(x => x.idx)
            .Select(x =>
            {
                var d = info[x.w];
                return new Candidate
                {
                    Word = x.w, Reading = d.Reading, MailCount = count[x.w], Category = d.Category,
                    FromSignature = sigWords.Contains(x.w), Unsure = d.Unsure, Example = d.Example, ShortReading = d.ShortReading,
                    Examples = [.. d.Examples],
                };
            })
            .ToList();
        if (settings.MaxPhrases > 0)
            rows.AddRange(Phrases(common, settings));
        return rows;
    }

    public const string PhraseCategory = "定型文";
    const int MaxExamples = 5;
    static readonly System.Text.RegularExpressions.Regex HasJapanese = new(@"[ぁ-んァ-ヶ一-龥]");

    /// <summary>
    /// 多くのメールに繰り返し出る行（署名・あいさつ）を、短縮よみで呼び出す定型文の候補にする。
    /// よみは自分で決めてもらう（「しょめい」→ 署名の1行 など）。IME の語の長さの上限（60文字）を超えるものは出さない
    /// </summary>
    static IEnumerable<Candidate> Phrases(Dictionary<string, int> common, ExtractorSettings settings) =>
        common.Where(kv => kv.Value >= settings.MinMails)
            .Select(kv => (Line: MailPreprocessor.Normalize(kv.Key).Trim(), Count: kv.Value))
            .Where(x => x.Line.Length is >= 6 and <= 60 && HasJapanese.IsMatch(x.Line))
            .GroupBy(x => x.Line).Select(g => (Line: g.Key, Count: g.Max(x => x.Count)))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Line, StringComparer.Ordinal)
            .Take(settings.MaxPhrases)
            .Select(x => new Candidate
            {
                Word = x.Line, Reading = "", MailCount = x.Count, Category = PhraseCategory,
                FromSignature = true, Unsure = true, Example = x.Line, Examples = [x.Line],
            });
}
