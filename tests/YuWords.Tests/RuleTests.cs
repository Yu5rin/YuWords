using YuWords.Core;

namespace YuWords.Tests;

public sealed class RuleTests : IClassFixture<TermExtractorTests.TokenizerFixture>, IDisposable
{
    readonly MorphTokenizer tok;
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_rule_{Guid.NewGuid():N}");

    public RuleTests(TermExtractorTests.TokenizerFixture f) => tok = f.Tokenizer;

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    Term Single(string text, params string[] rules) =>
        new TermExtractor(tok, new ExtractorSettings { ReadingRules = [.. rules] })
            .Extract(text).Select(x => x.Term).First(t => t.Parent == null);

    [Fact]
    public void ルールの書式を読む()
    {
        var map = TermExtractor.ParseRules(["係=がかり", " ＸＹＺ ＝ ミー ", "壊れた行", "=よみだけ", "漢字=漢字"]);
        Assert.Equal(new Dictionary<string, string> { ["係"] = "がかり", ["XYZ"] = "みー" }, map);
    }

    [Fact]
    public void 複合語の一部にもルールのよみを使う()
    {
        Assert.Equal("せっけいかかり", Single("設計係").Reading);
        Assert.Equal("せっけいがかり", Single("設計係", "係=がかり").Reading);
    }

    [Fact]
    public void ルールで決まった部分は推測の判定から外す()
    {
        var before = Single("XYZ設検課の件");
        Assert.True(before.Unsure);
        var after = Single("XYZ設検課の件", "XYZ=みー", "設検課=せつけんか");
        Assert.Equal("みーせつけんか", after.Reading);
        Assert.False(after.Unsure);
        // 一部だけルールで決まり、残りが推測なら要確認のまま
        Assert.True(Single("XYZ設検課の件", "XYZ=みー").Unsure);
    }

    [Fact]
    public void 長いルールを優先する()
    {
        Assert.Equal("とっかいか", Single("設検課", "設検課=とっかいか", "課=か").Reading);
    }

    [Fact]
    public void 前回直したよみを同じ語に使う()
    {
        var rows = new List<Candidate>
        {
            new() { Word = "桐電", Reading = "きりでん", Unsure = true, Category = "複合語" },
            new() { Word = "工符", Reading = "こうふ", Unsure = true, Category = "複合語" },
        };
        Assert.Equal(1, Extraction.ApplyLearned(rows, new Dictionary<string, string> { ["桐電"] = "ひさでん" }));
        Assert.Equal(("ひさでん", false, "前回の修正"), (rows[0].Reading, rows[0].Unsure, rows[0].ReadingSource));
        Extraction.ApplyImeReading(rows[0], "とうでん");
        Assert.False(rows[0].Unsure);   // IME と違っても、自分で直したよみを優先

        var s = new AppStore(dir);
        s.Learn("桐電", "ひさでん");
        Assert.Equal("ひさでん", new AppStore(dir).Learned["桐電"]);
        s.ClearLearned();
        Assert.Empty(new AppStore(dir).Learned);
    }

    [Fact]
    public void おすすめ度はIMEで変換できない語とよく使う語ほど高い()
    {
        ReviewItem I(string w, int count, string ime) =>
            new(new Candidate { Word = w, Reading = "よみ", Category = "複合語", MailCount = count, ImeConversion = ime }, false);
        var a = I("工符", 100, "公伴");           // 変換できない・よく使う
        var b = I("生産管理部", 100, "生産管理部"); // 変換できる
        var c = I("桐電", 3, "東電");              // 変換できない・まれ
        var d = I("検了", 50, "県間");
        d.IsRegistered = true;
        var items = new[] { a, b, c, d };
        Assert.True(a.Score > b.Score && a.Score > c.Score);
        Assert.Equal(0, d.Score);
        ReviewItem.Rank(items);
        Assert.Equal(3, a.Stars);
        Assert.Equal(0, d.Stars);
        Assert.InRange(c.Stars, 1, 2);
    }
}
