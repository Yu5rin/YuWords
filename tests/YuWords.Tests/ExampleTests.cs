using YuWords.Core;

namespace YuWords.Tests;

public sealed class ExampleTests : IClassFixture<TermExtractorTests.TokenizerFixture>
{
    readonly MorphTokenizer tok;

    public ExampleTests(TermExtractorTests.TokenizerFixture f) => tok = f.Tokenizer;

    [Fact]
    public void 例文は違う行を最大5件集める()
    {
        var mails = Enumerable.Range(0, 8).Select(i => new List<string> { $"工符{i}番の件", "工符の確認をお願いします" }).ToList();
        var rows = CandidateBuilder.Build(mails, new TermExtractor(tok, new ExtractorSettings()), new ExtractorSettings { MaxPhrases = 0 });
        var c = rows.Single(r => r.Word == "工符");
        Assert.Equal(5, c.Examples.Count);
        Assert.Equal(c.Examples.Distinct().Count(), c.Examples.Count);
        Assert.Equal(c.Example, c.Examples[0]);
    }

    [Fact]
    public void よみの一部を置き換える()
    {
        var a = new ReviewItem(new Candidate { Word = "設計係", Reading = "せっけいかかり", Unsure = true, Category = "複合語" }, false);
        Assert.True(a.ReplaceInReading("カカリ", "がかり"));
        Assert.Equal(("せっけいがかり", false, true), (a.Reading, a.Unsure, a.Register));
        Assert.False(a.ReplaceInReading("ない", "x"));
    }

    [Fact]
    public void 古い途中保存の例文も1行として見せる()
    {
        var a = new ReviewItem(new Candidate { Word = "工符", Reading = "こうふ", Category = "複合語", Example = "工符の件" }, false);
        Assert.Equal(["工符の件"], a.Examples);
    }
}
