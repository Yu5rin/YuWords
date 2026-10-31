using YuWords.Core;

namespace YuWords.Tests;

public sealed class ReadingAccuracyTests : IClassFixture<TermExtractorTests.TokenizerFixture>
{
    readonly MorphTokenizer tok;

    public ReadingAccuracyTests(TermExtractorTests.TokenizerFixture f) => tok = f.Tokenizer;

    [Fact]
    public void 複数の複合語に共通する部分を部分語の候補にする()
    {
        var mails = new List<List<string>>();
        for (int i = 0; i < 3; i++) mails.Add([$"件名{i}", $"標準コアユニット月次報告を送ります{i}"]);
        for (int i = 0; i < 3; i++) mails.Add([$"件名{i + 3}", $"標準コアユニット仕様書を更新しました{i}"]);
        var rows = CandidateBuilder.Build(mails, new TermExtractor(tok, new ExtractorSettings()), new ExtractorSettings());

        var sub = rows.Single(c => c.Word == "標準コアユニット");
        Assert.Equal("部分語", sub.Category);
        Assert.Equal(6, sub.MailCount);
        // 1種類の複合語にしか出ない部分（月次報告は 標準コアユニット月次報告 だけ）は出さない
        Assert.DoesNotContain(rows, c => c.Word == "コアユニット月次報告");
        Assert.Contains(rows, c => c.Word == "標準コアユニット月次報告");
    }

    [Fact]
    public void 部分語は単独で抽出された語の扱いを変えない()
    {
        var ex = new TermExtractor(tok, new ExtractorSettings());
        var terms = ex.Extract("品質保証課生産管理部長").Select(x => x.Term).ToList();
        var main = terms.Single(t => t.Parent == null && t.Word == "品質保証課生産管理部長");
        Assert.Equal("複合語", main.Category);
        Assert.Contains(terms, t => t.Parent == main.Word && t.Word == "品質保証課");
        Assert.All(terms.Where(t => t.Parent != null), t => Assert.Equal("部分語", t.Category));
    }

    [Fact]
    public void IMEのよみはよみ不明の語にだけ使う()
    {
        var a = new Candidate { Word = "鍛推", Reading = "", Unsure = true, Category = "複合語" };
        Extraction.ApplyImeReading(a, "きたえすい");
        Assert.Equal(("きたえすい", "IME"), (a.Reading, a.ReadingSource));

        var b = new Candidate { Word = "桐電", Reading = "きりでん", Unsure = true, Category = "複合語" };
        Extraction.ApplyImeReading(b, "とうでん");
        Assert.Equal("きりでん", b.Reading);
        Assert.True(new ReviewItem(b, false).ReadingsDiffer);

        var d = new Candidate { Word = "設計係", Reading = "せっけいかかり", Unsure = false, Category = "複合語" };
        Extraction.ApplyImeReading(d, "せっけいがかり");
        Assert.True(d.Unsure);                                 // 食い違えば要確認にして、登録チェックも付けない
        Assert.False(new ReviewItem(d, false).Register);

        var e = new Candidate { Word = "東海林", Reading = "とうかいりん", Unsure = false, Category = "人名", ReadingSource = "連絡先" };
        Extraction.ApplyImeReading(e, "しょうじ");
        Assert.False(e.Unsure);                                // 連絡先のふりがなを優先

        var c = new Candidate { Word = "XYZ設検課", Reading = "えっくすわいぜっとせつけんか", Category = "複合語" };
        Extraction.ApplyImeReading(c, "ＸＹＺせつけんか");   // 英字が残ったよみは使わない
        Assert.Equal("", c.ImeReading);
    }

    [Fact]
    public void 同じよみの表記ゆれを見つける()
    {
        var items = new[] { "打合せ", "打ち合わせ", "工符" }
            .Select((w, i) => new ReviewItem(new Candidate { Word = w, Reading = i < 2 ? "うちあわせ" : "こうふ", Category = "複合語" }, false))
            .ToList();
        ReviewItem.MarkVariants(items);
        Assert.Equal(["打ち合わせ"], items[0].Variants);
        Assert.Equal(["打合せ"], items[1].Variants);
        Assert.Empty(items[2].Variants);
        Assert.Equal(["打合せ", "打ち合わせ"], items.Where(new ReviewFilter { CheckReadingOnly = true }.Matches).Select(i => i.Word));
    }
}
