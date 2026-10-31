using YuWords.Core;

namespace YuWords.Tests;

/// <summary>辞書の手入れ（使わなくなった語・よみの直し・人名の品詞）。例はすべて架空</summary>
public sealed class DictionaryCareTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_care_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void 文章に出てくる登録済みの語を見つける()
    {
        List<List<string>> docs =
        [
            ["お疲れさまです。ＸＹＺ設検課の件です。"],   // 全角でも見つける
            ["工符の確認をお願いします", "以上"],
        ];
        var found = WordUsage.Find(docs, ["XYZ設検課", "工符", "図締", "設検", ""]);
        Assert.Equal(["XYZ設検課", "工符", "設検"], found.Order().ToArray());
        Assert.Empty(WordUsage.Find(docs, []));
    }

    [Fact]
    public void 長いあいだ出てこない語を使わなくなったかもしれない語にする()
    {
        var day0 = new DateTime(2026, 1, 1);
        RegisteredWord W(string w, int registeredDay, string pos = "名詞") => new(w, "よみ", pos, day0.AddDays(registeredDay));
        var registered = new[] { W("工符", 0), W("図締", 0), W("盤検", 150), W("お世話になっております。", 0, "短縮よみ") };
        var lastUsed = new Dictionary<string, DateTime> { ["図締"] = day0.AddDays(100) };

        Assert.Empty(WordUsage.Stale(registered, lastUsed, null, 180));   // まだ文章を調べていない

        var stale = WordUsage.Stale(registered, lastUsed, day0.AddDays(200), 180);
        Assert.Equal(["工符"], stale.Select(r => r.Word));   // 図締は 100 日目に見た、盤検は 150 日目に登録、定型文は対象外
        Assert.Equal(day0.AddDays(100), WordUsage.LastSeen(registered[1], lastUsed));
        Assert.Equal(day0, WordUsage.LastSeen(registered[0], lastUsed));
    }

    [Fact]
    public void 見かけた日と調べた日を保存する()
    {
        var s = new AppStore(dir);
        var when = new DateTime(2026, 10, 1, 9, 0, 0);
        s.MarkUsed(["工符"], when);
        var again = new AppStore(dir);
        Assert.Equal(when, again.LastUsed["工符"]);
        Assert.Equal(when, again.Settings.LastUsageScan);
    }

    [Fact]
    public void 登録済みの語のよみと品詞を直す()
    {
        var s = new AppStore(dir);
        s.AddRegistered([("桐電", "きりでん", "名詞")], GoogleIme.Target);
        var old = s.Registered.Single();

        // Google に書き出した語は、IME に触らず一覧だけ直す
        Assert.True(ImeRegistration.Edit(old, "キリデンキ", "固有名詞", s));
        var now = new AppStore(dir).Registered.Single();
        Assert.Equal(("桐電", "きりでんき", "固有名詞", old.RegisteredAt), (now.Word, now.Reading, now.Pos, now.RegisteredAt));

        Assert.False(ImeRegistration.Edit(s.Registered.Single(), "きりでんき", "固有名詞", s));   // 変わっていない
        Assert.False(ImeRegistration.Edit(s.Registered.Single(), " ", "名詞", s));               // よみが空
    }

    [Fact]
    public void 直したよみがほかの行と重なるときは古い行を外す()
    {
        var s = new AppStore(dir);
        s.AddRegistered([("桐電", "きりでん", "名詞"), ("桐電", "きりでんき", "名詞")], GoogleIme.Target);
        ImeRegistration.Edit(s.Registered[0], "きりでんき", "名詞", s);
        Assert.Equal([("桐電", "きりでんき")], s.Registered.Select(r => (r.Word, r.Reading)));
    }

    [Fact]
    public void 人名と分かる語は最初から品詞を人名にする()
    {
        string Pos(string category, string source = "") =>
            new ReviewItem(new Candidate { Word = "架空", Reading = "かくう", Category = category, ReadingSource = source }, false).Pos;
        Assert.Equal("人名", Pos("人名"));
        Assert.Equal("人名", Pos("複合語", NameReadings.Source));   // 連絡先のふりがなでよみを補った語
        Assert.Equal("地名", Pos("地名"));
        Assert.Equal("名詞", Pos("複合語"));
        Assert.Equal("短縮よみ", Pos(CandidateBuilder.PhraseCategory));
    }
}
