namespace YuWords.Core;

/// <summary>確認画面の1行。候補に、ユーザーの選択（登録・品詞・修正したよみ）を足したもの</summary>
public sealed class ReviewItem
{
    /// <summary>選べる品詞（Microsoft IME ユーザー辞書ツールの品詞名）</summary>
    public static readonly string[] PosChoices = ["名詞", "人名", "地名", "固有名詞", "短縮よみ"];

    public ReviewItem(Candidate c, bool registered)
    {
        Source = c;
        Word = c.Word;
        Reading = c.Reading;
        Unsure = c.Unsure;
        IsRegistered = registered;
        Register = !c.Unsure && c.Reading.Length > 0 && !registered;
        ImeConversion = c.ImeConversion;
        if (c.Category == CandidateBuilder.PhraseCategory) Pos = "短縮よみ";
        ExtraReadings = c.ExtraReadings.Where(r => r.Length > 0 && r != Reading).Distinct().ToList();
    }

    /// <summary>本来のよみのほかに登録するよみ（短いよみ・別の言い方）</summary>
    public List<string> ExtraReadings { get; private set; } = [];

    /// <summary>長い語の短いよみの提案</summary>
    public string ShortReading => Source.ShortReading;

    /// <summary>別のよみを設定する（「、」や空白・カンマ区切り）。カタカナはひらがなにし、本来のよみと同じものは除く</summary>
    public void SetExtraReadings(string text)
    {
        ExtraReadings = System.Text.RegularExpressions.Regex.Split(text, @"[、,，\s]+")
            .Select(r => TermExtractor.KataToHira(r.Trim())).Where(r => r.Length > 0 && r != Reading).Distinct().ToList();
    }

    /// <summary>短いよみの提案を別のよみに足す</summary>
    public bool AddShortReading()
    {
        if (ShortReading.Length == 0 || ShortReading == Reading || ExtraReadings.Contains(ShortReading)) return false;
        ExtraReadings = [.. ExtraReadings, ShortReading];
        return true;
    }

    /// <summary>登録する（よみ・語句・品詞）の組。本来のよみと別のよみの両方</summary>
    public IEnumerable<(string Reading, string Word, string Pos)> Entries()
    {
        if (Reading.Length > 0) yield return (Reading, Word, Pos);
        foreach (var r in ExtraReadings) yield return (r, Word, Pos);
    }

    /// <summary>おすすめ度（★の数 1〜3）。登録済み・保留は 0</summary>
    public int Stars { get; set; }

    /// <summary>
    /// 登録する価値の目安。IME が変換できない語ほど、よく使う語ほど高い。
    /// 変換できる語も予測変換に出す意味はあるので 0 にはしない
    /// </summary>
    public double Score
    {
        get
        {
            if (IsRegistered || Held) return 0;
            var s = Math.Log2(MailCount + 1);
            // 定型文は IME の変換の可否に関係なく、短縮よみを付けたいものだけ選んでもらう
            if (Category == CandidateBuilder.PhraseCategory) return s * 0.8;
            if (!ImeConvertible) s *= 3.0;
            if (Category == "部分語") s *= 0.8;
            return s;
        }
    }

    /// <summary>おすすめ度の高い順に、上位 2 割を ★★★、次の 3 割を ★★、残りを ★ にする</summary>
    public static void Rank(IReadOnlyList<ReviewItem> items)
    {
        var ranked = items.Where(i => i.Score > 0).OrderByDescending(i => i.Score).ToList();
        foreach (var i in items) i.Stars = 0;
        for (int k = 0; k < ranked.Count; k++)
            ranked[k].Stars = k < ranked.Count * 0.2 ? 3 : k < ranked.Count * 0.5 ? 2 : 1;
    }

    /// <summary>同じよみで表記が違う候補どうしに、互いの表記を記録する</summary>
    public static void MarkVariants(IEnumerable<ReviewItem> items)
    {
        foreach (var g in items.Where(i => i.Reading.Length > 0).GroupBy(i => i.Reading))
        {
            var words = g.Select(i => i.Word).Distinct().ToList();
            foreach (var i in g)
                i.Variants = words.Count > 1 ? words.Where(w => w != i.Word).ToList() : [];
        }
        foreach (var i in items.Where(i => i.Reading.Length == 0)) i.Variants = [];
    }

    public Candidate Source { get; }
    public bool Register { get; set; }
    public string Word { get; set; }
    public string Reading { get; private set; }
    public string Pos { get; set; } = "名詞";
    public bool Unsure { get; private set; }
    /// <summary>
    /// 今の語句・よみで登録済みか。IME のユーザー辞書にあると分かったのは抽出したときの語句・よみなので、
    /// 語句やよみを変えたらそれには当たらない
    /// </summary>
    public bool ComputeRegistered(Func<string, string, bool> inRegisteredList) =>
        inRegisteredList(Word, Reading) || (InImeDictionary && Word == Source.Word && Reading == Source.Reading);

    /// <summary>以前に登録（出力）した語か、IME のユーザー辞書に既にある語か</summary>
    public bool IsRegistered { get; set; }
    /// <summary>IME のユーザー辞書に既にある語か（YuWords 以外で登録したものも含む）</summary>
    public bool InImeDictionary { get; set; }
    /// <summary>見終えた行か（値を変えたり、確認済みにしたりすると付く）</summary>
    public bool Reviewed { get; set; }
    /// <summary>後回しにした語か（次回以降も、選んだよみ・品詞を残したまま保留として扱う）</summary>
    public bool Held { get; set; }

    public int MailCount => Source.MailCount;
    public string Category => Source.Category;
    public bool FromSignature => Source.FromSignature;
    /// <summary>IME の変換結果。よみを直したら取り直した結果に置き換わる</summary>
    public string ImeConversion { get; set; } = "";
    public bool ImeConvertible => ImeConversion == Word;
    /// <summary>IME に表記を渡したときのよみ</summary>
    public string ImeReading => Source.ImeReading;
    /// <summary>IME のよみが今のよみと違う（どちらが正しいか確かめたい語）</summary>
    public bool ReadingsDiffer => ImeReading.Length > 0 && ImeReading != Reading;
    /// <summary>同じよみで表記が違う候補（表記ゆれ）</summary>
    public List<string> Variants { get; set; } = [];
    public string Example => Source.Example;
    /// <summary>例文（最大 5 行）。古い途中保存などで無ければ代表の 1 行</summary>
    public IReadOnlyList<string> Examples => Source.Examples.Count > 0 ? Source.Examples : Source.Example.Length > 0 ? [Source.Example] : [];

    /// <summary>よみの一部を置き換える（かかり → がかり）。変わったら true（要確認を外し、登録を ON にする）</summary>
    public bool ReplaceInReading(string from, string to)
    {
        from = TermExtractor.KataToHira(from);
        to = TermExtractor.KataToHira(to);
        if (from.Length == 0 || !Reading.Contains(from)) return false;
        EditReading(Reading.Replace(from, to));
        return true;
    }

    /// <summary>ユーザーが変えられる値の控え（元に戻す・途中保存に使う）</summary>
    public sealed record State(bool Register, string Word, string Reading, bool Unsure, string Pos, string ImeConversion,
        string Extra = "", bool Reviewed = false, bool Held = false, bool InIme = false);

    public State Save() => new(Register, Word, Reading, Unsure, Pos, ImeConversion, string.Join("、", ExtraReadings), Reviewed, Held,
        InImeDictionary);

    public void Restore(State s)
    {
        (Register, Word, Reading, Unsure, Pos, ImeConversion) = (s.Register, s.Word, s.Reading, s.Unsure, s.Pos, s.ImeConversion);
        ExtraReadings = s.Extra.Length == 0 ? [] : [.. s.Extra.Split('、')];
        (Reviewed, Held, InImeDictionary) = (s.Reviewed, s.Held, s.InIme);
    }

    /// <summary>前の一覧での選択（登録・語句・よみの修正・品詞）を引き継ぐ</summary>
    public void CopyChoicesFrom(ReviewItem old)
    {
        Word = old.Word;
        Pos = old.Pos;
        ExtraReadings = [.. old.ExtraReadings];
        (Reviewed, Held) = (old.Reviewed, old.Held);
        if (old.Reading != old.Source.Reading)
        {
            EditReading(old.Reading);
            ImeConversion = old.ImeConversion;   // 変換結果は直したよみで取ったもの
        }
        Register = old.Register && Reading.Length > 0;
    }

    /// <summary>よみを手で直した。要確認を外し、登録を ON にする</summary>
    public void EditReading(string reading)
    {
        reading = TermExtractor.KataToHira(reading.Trim());
        if (reading == Reading) return;
        Reading = reading;
        Unsure = false;
        Register = reading.Length > 0;
    }
}

/// <summary>確認画面の絞り込み条件</summary>
public sealed class ReviewFilter
{
    public bool UnsureOnly { get; set; }
    public bool SignatureOnly { get; set; }
    public bool NotConvertibleOnly { get; set; }
    public bool CheckedOnly { get; set; }
    /// <summary>IME のよみと食い違う語、または表記ゆれのある語だけ</summary>
    public bool CheckReadingOnly { get; set; }
    public bool UnreviewedOnly { get; set; }
    public bool ShowHeld { get; set; }
    public bool ShowRegistered { get; set; } = true;
    public string Search { get; set; } = "";

    public bool Matches(ReviewItem i)
    {
        if (UnsureOnly && !i.Unsure) return false;
        if (SignatureOnly && !i.FromSignature) return false;
        if (NotConvertibleOnly && i.ImeConvertible) return false;
        if (CheckedOnly && !i.Register) return false;
        if (UnreviewedOnly && (i.Reviewed || i.IsRegistered)) return false;
        if (!ShowHeld && i.Held) return false;
        if (CheckReadingOnly && !i.ReadingsDiffer && i.Variants.Count == 0) return false;
        if (!ShowRegistered && i.IsRegistered) return false;
        if (Search.Length > 0 && !i.Word.Contains(Search, StringComparison.OrdinalIgnoreCase)
                              && !i.Reading.Contains(Search, StringComparison.Ordinal))
            return false;
        return true;
    }
}
