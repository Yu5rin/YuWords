using System.Text;
using System.Text.RegularExpressions;

namespace YuWords.Core;

/// <summary>抽出した語1つ分</summary>
/// <param name="Word">語句</param>
/// <param name="Reading">よみ（ひらがな）。不明なら空</param>
/// <param name="Unsure">よみが推測で要確認か</param>
/// <param name="Category">分類（複合語 / 普通名詞 / 人名 など）</param>
/// <param name="IsCommon">辞書にある1語の普通名詞（登録しても意味がない）</param>
/// <param name="Parent">長い複合語の一部として作った語なら、その複合語。普通の語は null</param>
/// <param name="ShortReading">長い語の短いよみの提案（各語の頭の1音）。提案できなければ空</param>
public sealed record Term(string Word, string Reading, bool Unsure, string Category, bool IsCommon, string? Parent = null,
    string ShortReading = "");

/// <summary>名詞の連結・トリム・ノイズ除外・よみ生成（参照実装の extract_terms 以下）</summary>
public sealed class TermExtractor
{
    static readonly HashSet<string> BaseStopWords =
    [
        "お世話", "よろしく", "お願い", "確認", "連絡", "件", "以上", "皆様", "各位",
        "本日", "明日", "今回", "対応", "ご確認", "宜しく",
    ];

    // 複合語の前後から外す語（「通り回答」→「回答」、「高田様」→「高田」など）
    static readonly HashSet<string> BaseLeadStrip =
    [
        "通り", "とおり", "以下", "下記", "上記", "こと", "添付", "本件", "今回",
        "本日", "その後", "いつも", "皆様", "各位",
    ];
    static readonly HashSet<string> TrailStrip = ["様", "さん", "殿", "各位", "等", "宛"];

    static readonly Dictionary<char, string> AlphaReading = new()
    {
        ['A'] = "えー", ['B'] = "びー", ['C'] = "しー", ['D'] = "でぃー", ['E'] = "いー", ['F'] = "えふ",
        ['G'] = "じー", ['H'] = "えいち", ['I'] = "あい", ['J'] = "じぇー", ['K'] = "けー", ['L'] = "える",
        ['M'] = "えむ", ['N'] = "えぬ", ['O'] = "おー", ['P'] = "ぴー", ['Q'] = "きゅー", ['R'] = "あーる",
        ['S'] = "えす", ['T'] = "てぃー", ['U'] = "ゆー", ['V'] = "ぶい", ['W'] = "だぶりゅー",
        ['X'] = "えっくす", ['Y'] = "わい", ['Z'] = "ぜっと",
    };
    static readonly Dictionary<char, string> DigitReading = new()
    {
        ['0'] = "ぜろ", ['1'] = "いち", ['2'] = "に", ['3'] = "さん", ['4'] = "よん",
        ['5'] = "ご", ['6'] = "ろく", ['7'] = "なな", ['8'] = "はち", ['9'] = "きゅう",
    };

    static readonly Regex AsciiOnly = new(@"^[\x00-\x7F]+$");
    static readonly Regex HiraOnly = new(@"^[ぁ-ん]+$");
    static readonly Regex ReadingOk = new(@"^[ぁ-んー]+$");
    static readonly Regex SymbolsOnly = new(@"^[\d\W_ー]+$");
    static readonly Regex HasSymbol = new(@"[()（）「」【】\[\]<>＜＞/／:：・,、。]");
    static readonly Regex Kanji = new(@"^[一-龥]");
    static readonly Regex UnknownNoun = new(@"^(?![0-9]+$)[A-Za-z0-9ァ-ヶー一-龥]+$");   // 数字だけ（数詞）は除く

    readonly MorphTokenizer tokenizer;
    readonly HashSet<string> stopWords;
    readonly HashSet<string> leadStrip;
    readonly int maxCompoundLength;
    readonly Dictionary<string, string> rules;

    public TermExtractor(MorphTokenizer tokenizer, ExtractorSettings settings)
    {
        this.tokenizer = tokenizer;
        stopWords = [.. BaseStopWords, .. settings.ExtraStopWords];
        leadStrip = [.. BaseLeadStrip, .. settings.ExtraLeadStrip];
        maxCompoundLength = settings.MaxCompoundLength;
        rules = ParseRules(settings.ReadingRules);
    }

    public static string KataToHira(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c >= 'ァ' && c <= 'ヶ' ? (char)(c - 0x60) : c);
        return sb.ToString();
    }

    static bool IsAscii(string s) => AsciiOnly.IsMatch(s);

    /// <summary>英数字を1文字ずつアルファベット読み・数字読みにする（MOP → えむおーぴー）。読めない文字があれば null</summary>
    public static string? AlphabetReading(string ascii)
    {
        var sb = new StringBuilder();
        foreach (var ch in ascii.ToUpperInvariant())
        {
            if (AlphaReading.TryGetValue(ch, out var a)) sb.Append(a);
            else if (DigitReading.TryGetValue(ch, out var d)) sb.Append(d);
            else if (ch is '-' or '_') sb.Append('ー');
            else return null;
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>1トークンのよみ。英数字はアルファベット読みにする。作れなければ null</summary>
    static string? TokenReading(Token t)
    {
        var s = t.Surface;
        if (IsAscii(s)) return AlphabetReading(s);
        if (!string.IsNullOrEmpty(t.Kana))
            return KataToHira(t.Kana);
        // 未知語は辞書に仮名形がない。仮名だけの語なら表記をそのままよみにする（Sudachi と同じ）
        return KataToHira(s);
    }

    static readonly HashSet<char> SmallKana = ['ぁ', 'ぃ', 'ぅ', 'ぇ', 'ぉ', 'ゃ', 'ゅ', 'ょ', 'ゎ'];

    /// <summary>各語のよみの頭の1音をつなげる（ひんしつ・ほしょう・か → ひほか、しゅっか・よてい → しゅよ）</summary>
    public static string ShortReadingOf(IEnumerable<string?> partReadings)
    {
        var sb = new StringBuilder();
        foreach (var p in partReadings)
        {
            if (string.IsNullOrEmpty(p) || p[0] == 'ー' || p[0] == 'っ') continue;
            sb.Append(p[0]);
            if (p.Length > 1 && SmallKana.Contains(p[1])) sb.Append(p[1]);
        }
        return sb.ToString();
    }

    bool IsNoise(string w)
    {
        if (w.Length < 2 || stopWords.Contains(w)) return true;
        if (IsAscii(w)) return true;
        if (SymbolsOnly.IsMatch(w)) return true;
        if (HiraOnly.IsMatch(w)) return true;
        if (HasSymbol.IsMatch(w)) return true;   // 記号を含む
        return false;
    }

    static bool IsNounPart(Token t)
    {
        if (t.Pos1 == "名詞" && t.Pos2 != "数詞") return true;
        // 未知語は文脈で感動詞などにされることがある（「。XYZ鍛推」の XYZ）。Sudachi と同じく名詞扱い
        if (t.IsUnknown && UnknownNoun.IsMatch(t.Surface)) return true;
        // UniDic は単独の漢字（電・推 など）を「記号,一般」にすることがある。社内略語の一部なので名詞扱い
        if (t.Pos1 == "記号" && t.Pos2 == "一般" && t.Surface.Length == 1 && Kanji.IsMatch(t.Surface)) return true;
        return t.Pos1 == "接尾辞" && t.Pos2 == "名詞的";   // 部・課・係・室・様 など
    }

    List<Token> Trim(List<Token> buf)
    {
        int start = 0, end = buf.Count;
        while (start < end && (buf[start].Pos1 == "接尾辞"
                               || leadStrip.Contains(buf[start].Surface)
                               || HiraOnly.IsMatch(buf[start].Surface)
                               || (buf[start].Surface.Length == 1 && buf[start].Pos3 == "助数詞可能")))
            start++;
        while (start < end && TrailStrip.Contains(buf[end - 1].Surface))
            end--;
        return buf.GetRange(start, end - start);
    }

    /// <summary>
    /// 各部分のよみ。よみのルールに合う連続したトークン（長いものを優先）はひとまとまりにしてルールのよみを使う。
    /// 戻り値の Covered は、ルールでよみが決まったトークン
    /// </summary>
    (List<string?> Parts, bool[] Covered) PartReadings(List<Token> buf)
    {
        var parts = new List<string?>();
        var covered = new bool[buf.Count];
        for (int i = 0; i < buf.Count;)
        {
            int matched = 0;
            string? ruleReading = null;
            if (rules.Count > 0)
            {
                for (int j = Math.Min(buf.Count, i + 8); j > i; j--)
                {
                    var key = string.Concat(buf.Skip(i).Take(j - i).Select(x => x.Surface));
                    if (rules.TryGetValue(key, out ruleReading)) { matched = j - i; break; }
                }
            }
            if (matched > 0)
            {
                parts.Add(ruleReading);
                for (int k = i; k < i + matched; k++) covered[k] = true;
                i += matched;
            }
            else
            {
                parts.Add(TokenReading(buf[i]));
                i++;
            }
        }
        return (parts, covered);
    }

    /// <summary>「表記=よみ」の行を読む（よみはひらがなにする。書式が違う行は飛ばす）</summary>
    public static Dictionary<string, string> ParseRules(IEnumerable<string> lines)
    {
        var map = new Dictionary<string, string>();
        foreach (var line in lines)
        {
            var i = line.IndexOfAny(['=', '＝']);
            if (i <= 0) continue;
            var key = MailPreprocessor.Normalize(line[..i].Trim());
            var reading = KataToHira(MailPreprocessor.Normalize(line[(i + 1)..].Trim()));
            if (key.Length > 0 && ReadingOk.IsMatch(reading)) map[key] = reading;
        }
        return map;
    }

    /// <summary>トークン列から語を作る。不適なら null</summary>
    Term? MakeTerm(List<Token> buf)
    {
        buf = Trim(buf);
        if (buf.Count == 0) return null;
        var w = string.Concat(buf.Select(x => x.Surface));
        if (w.Length > maxCompoundLength || IsNoise(w)) return null;

        // よみのルール（自分で決めた「表記=よみ」）に合う部分はルールのよみを使い、推測の判定からも外す
        var (parts, covered) = PartReadings(buf);
        var reading = parts.Contains(null) ? "" : string.Concat(parts);
        if (!ReadingOk.IsMatch(reading))
            reading = "";          // 漢字や記号が残る＝よみ不明。取り込みファイルには出さない

        var guessed = buf.Where((_, i) => !covered[i]).ToList();
        bool oov = guessed.Any(x => x.IsUnknown);
        bool hasAscii = guessed.Any(x => IsAscii(x.Surface));
        // 漢字1文字ずつに分割された箇所が2つ以上 → 辞書にない略語。よみは推測
        int fragments = guessed.Count(x => x.Surface.Length == 1 && Kanji.IsMatch(x.Surface));
        bool hasName = guessed.Any(x => x.Pos3 == "人名");

        string category;
        if (buf.Count > 1)
            category = "複合語";
        else
            category = buf[0].Pos2 == "固有名詞" ? buf[0].Pos3 : buf[0].Pos2;

        // 辞書にある1語の普通名詞（変更・完了など）は登録しても意味がない
        bool common = buf.Count == 1 && !buf[0].IsUnknown && !covered[0] && buf[0].Pos2 == "普通名詞";
        bool unsure = oov || hasAscii || fragments >= 2 || hasName || reading.Length == 0;
        var shortReading = reading.Length >= 5 && parts.Count >= 2 && parts.All(p => !string.IsNullOrEmpty(p))
            ? ShortReadingOf(parts!) : "";
        return new Term(w, reading, unsure, category, common, ShortReading: shortReading);
    }

    /// <summary>複合語の先頭側・末尾側の部分（2語以上）。「標準コアユニット月次報告」→「標準コアユニット」「月次報告」など</summary>
    IEnumerable<Term> SubTerms(List<Token> tb, string parent)
    {
        var seen = new HashSet<string> { parent };
        for (int len = tb.Count - 1; len >= 2; len--)
        {
            foreach (var range in new[] { tb.GetRange(0, len), tb.GetRange(tb.Count - len, len) })
            {
                if (MakeTerm(range) is { } t && !t.IsCommon && seen.Add(t.Word))
                    yield return t with { Parent = parent, Category = "部分語" };
            }
        }
    }

    /// <summary>テキスト（複数行可）から語を抽出する。各語と、それが出た行を返す</summary>
    public List<(Term Term, string Line)> Extract(string text)
    {
        var terms = new List<(Term, string)>();
        foreach (var line in MailPreprocessor.Normalize(text).Split('\n'))
        {
            if (line.Length == 0) continue;
            var buf = new List<Token>();
            var tokens = tokenizer.Tokenize(line.Length > 10000 ? line[..10000] : line);
            for (int i = 0; i <= tokens.Count; i++)
            {
                var m = i < tokens.Count ? tokens[i] : null;
                if (m != null && IsNounPart(m))
                {
                    buf.Add(m);
                    continue;
                }
                if (buf.Count > 0)
                {
                    if (MakeTerm(buf) is { } t)
                        terms.Add((t, line));
                    // 英字で始まる複合語は、英字を除いた部分も候補にする（XYZ設検課 → 設検課）
                    var tb = Trim(buf);
                    Term? t2 = null;
                    if (tb.Count > 1 && IsAscii(tb[0].Surface) && MakeTerm(tb.GetRange(1, tb.Count - 1)) is { } x)
                        terms.Add((t2 = x, line));
                    // 4語以上の長い複合語は、先頭側・末尾側の部分も「部分語」として出す（集計側で、複数の複合語に共通するものだけ残す）
                    // 長すぎて候補にならない複合語からも部分語は作る
                    if (tb.Count >= 4)
                        foreach (var sub in SubTerms(tb, string.Concat(tb.Select(x => x.Surface))))
                            if (sub.Word != t2?.Word) terms.Add((sub, line));
                }
                buf = [];
            }
        }
        return terms;
    }
}
