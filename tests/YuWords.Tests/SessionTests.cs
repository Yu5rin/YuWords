using YuWords.Core;

namespace YuWords.Tests;

public sealed class SessionTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_ses_{Guid.NewGuid():N}");

    public SessionTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void 途中の選択を保存して再開できる()
    {
        var a = new ReviewItem(new Candidate { Word = "桐電", Reading = "きりでん", Unsure = true, Category = "複合語", MailCount = 7, Example = "桐電へ報告", ImeReading = "とうでん" }, false);
        a.EditReading("ひさでん");
        a.Pos = "固有名詞";
        a.ImeConversion = "桐電";
        var b = new ReviewItem(new Candidate { Word = "工符", Reading = "こうふ", Category = "複合語", MailCount = 3 }, false) { Register = false };

        var s = ReviewSession.From([a, b]);
        s.Sources = ["Outlook の送信済みメール"];
        s.CountHeader = "出現数";
        s.Save(dir);

        var loaded = ReviewSession.Load(dir)!;
        Assert.Equal(["Outlook の送信済みメール"], loaded.Sources);
        Assert.Equal("出現数", loaded.CountHeader);
        var items = loaded.ToItems((w, r) => w == "工符");
        Assert.Equal(("桐電", "ひさでん", "固有名詞", true, false, 7), (items[0].Word, items[0].Reading, items[0].Pos, items[0].Register, items[0].Unsure, items[0].MailCount));
        Assert.Equal(("桐電", "とうでん", "桐電へ報告"), (items[0].ImeConversion, items[0].ImeReading, items[0].Example));
        Assert.False(items[1].Register);
        Assert.True(items[1].IsRegistered);
    }

    [Fact]
    public void 壊れた途中保存は無視する()
    {
        File.WriteAllText(ReviewSession.PathIn(dir), "{ broken");
        Assert.Null(ReviewSession.Load(dir));
        ReviewSession.Delete(dir);
        Assert.False(File.Exists(ReviewSession.PathIn(dir)));
    }

    [Fact]
    public void 控えから元に戻せる()
    {
        var a = new ReviewItem(new Candidate { Word = "図締", Reading = "ずしめ", Unsure = true, Category = "複合語" }, false);
        var before = a.Save();
        a.EditReading("ずご");
        a.Pos = "人名";
        a.Restore(before);
        Assert.Equal(("ずしめ", true, false, "名詞"), (a.Reading, a.Unsure, a.Register, a.Pos));
    }
}
