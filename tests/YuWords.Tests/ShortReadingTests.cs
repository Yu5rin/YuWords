using YuWords.Core;

namespace YuWords.Tests;

public sealed class ShortReadingTests : IClassFixture<TermExtractorTests.TokenizerFixture>
{
    readonly MorphTokenizer tok;

    public ShortReadingTests(TermExtractorTests.TokenizerFixture f) => tok = f.Tokenizer;

    [Theory]
    [InlineData(new[] { "ひんしつ", "ほしょう", "か" }, "ひほか")]
    [InlineData(new[] { "しゅっか", "よてい" }, "しゅよ")]
    [InlineData(new[] { "えっくすわいぜっと", "とく", "い", "か" }, "えといか")]
    public void 各語の頭の1音をつなげる(string[] parts, string expected) =>
        Assert.Equal(expected, TermExtractor.ShortReadingOf(parts));

    [Fact]
    public void 長い複合語には短いよみを提案する()
    {
        var ex = new TermExtractor(tok, new ExtractorSettings());
        var t = ex.Extract("品質保証課へ").Select(x => x.Term).Single();
        Assert.Equal("ひほか", t.ShortReading);
        Assert.Equal("", ex.Extract("工符").Select(x => x.Term).Single().ShortReading);   // 短い語には出さない
    }

    [Fact]
    public void 繰り返し出る行を定型文の候補にする()
    {
        var mails = Enumerable.Range(0, 10).Select(i => new List<string>
        {
            $"件名{i}", "いつもお世話になっております。", $"本文{i}です", "生産管理部 山田太郎 内線1234", "TEL 0000",
        }).ToList();
        var rows = CandidateBuilder.Build(mails, new TermExtractor(tok, new ExtractorSettings()), new ExtractorSettings());
        var phrases = rows.Where(c => c.Category == CandidateBuilder.PhraseCategory).Select(c => c.Word).ToList();
        Assert.Contains("いつもお世話になっております。", phrases);
        Assert.Contains("生産管理部 山田太郎 内線1234", phrases);
        Assert.DoesNotContain("TEL 0000", phrases);   // 日本語を含まない行は出さない

        var item = new ReviewItem(rows.First(c => c.Category == CandidateBuilder.PhraseCategory), false);
        Assert.Equal(("短縮よみ", "", false), (item.Pos, item.Reading, item.Register));

        var none = CandidateBuilder.Build(mails, new TermExtractor(tok, new ExtractorSettings()), new ExtractorSettings { MaxPhrases = 0 });
        Assert.DoesNotContain(none, c => c.Category == CandidateBuilder.PhraseCategory);
    }

    [Fact]
    public void 定型文にはIMEの文のよみを使わない()
    {
        var c = new Candidate { Word = "いつもお世話になっております。", Reading = "", Unsure = true, Category = CandidateBuilder.PhraseCategory };
        Extraction.ApplyImeReading(c, "いつもおせわになっております");
        Assert.Equal("", c.Reading);
    }

    [Fact]
    public void 別のよみも登録の組に入る()
    {
        var i = new ReviewItem(new Candidate { Word = "品質保証課", Reading = "ひんしつほしょうか", Category = "複合語", ShortReading = "ひほか" }, false);
        Assert.True(i.AddShortReading());
        Assert.False(i.AddShortReading());
        i.SetExtraReadings("ひほか、ヒンホ, ひんしつほしょうか");
        Assert.Equal(["ひほか", "ひんほ"], i.ExtraReadings);
        Assert.Equal([("ひんしつほしょうか", "品質保証課", "名詞"), ("ひほか", "品質保証課", "名詞"), ("ひんほ", "品質保証課", "名詞")], i.Entries());

        var saved = i.Save();
        var j = new ReviewItem(i.Source, false);
        j.Restore(saved);
        Assert.Equal(["ひほか", "ひんほ"], j.ExtraReadings);
    }
}
