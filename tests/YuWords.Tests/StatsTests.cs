using YuWords.Core;

namespace YuWords.Tests;

/// <summary>これまでの効果の集計と、変換できなかった語の印・送信メールでの使われ方。例はすべて架空</summary>
public sealed class StatsTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_stats_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void 語ごとに出たメールの数を数える()
    {
        List<List<string>> docs = [["工符と工符の件", "図締"], ["工符"], ["ＸＹＺ設検課"]];
        var counts = WordUsage.Count(docs, ["工符", "図締", "XYZ設検課", "盤検"]);
        Assert.Equal(2, counts["工符"]);   // 1通に2回出ても1
        Assert.Equal(1, counts["図締"]);
        Assert.Equal(1, counts["XYZ設検課"]);
        Assert.False(counts.ContainsKey("盤検"));
    }

    [Fact]
    public void 効果を集計する()
    {
        var s = new AppStore(dir);
        var now = new DateTime(2026, 10, 31);
        s.AddRegistered([("工符", "こうふ", "名詞"), ("工符", "こうふう", "名詞"), ("高田", "たかだ", "人名"), ("図締", "ずしめ", "名詞")]);
        s.AddRegistered([("盤検", "ばんけん", "名詞")], GoogleIme.Target);
        for (int i = 0; i < s.Registered.Count; i++)
            s.Registered[i] = s.Registered[i] with { RegisteredAt = s.Registered[i].Word == "図締" ? new DateTime(2026, 8, 3) : new DateTime(2026, 10, 1) };
        s.MarkRescued(["工符", "盤検"]);
        s.MarkUsed(new Dictionary<string, int> { ["工符"] = 7, ["高田"] = 2, ["消した語"] = 9 }, 50, now);

        var st = UsageStats.Compute(s, now);
        Assert.Equal((4, 3, 1, 2), (st.Total, st.MicrosoftIme, st.Google, st.Rescued));   // よみ違いの工符は1語と数える
        Assert.Equal(12, st.ByMonth.Count);
        Assert.Equal(("2026/10", 3), st.ByMonth[^1]);
        Assert.Equal(("2026/08", 1), st.ByMonth[^3]);
        Assert.Equal(("名詞", 3), st.ByPos[0]);
        Assert.Equal([("工符", 7), ("高田", 2)], st.TopUsed);   // 登録していない語は出さない
        Assert.Equal((50, 2), (st.RecentMails, st.UsedInRecent));

        var again = new AppStore(dir);   // 保存されている
        Assert.Equal(3, again.Registered.Count(r => r.Rescued));   // よみ違いの工符の2行とも
        Assert.Equal(7, again.RecentUse.Counts["工符"]);
    }

    [Fact]
    public void 何も登録していなければ空()
    {
        var st = UsageStats.Compute(new AppStore(dir), DateTime.Now);
        Assert.Equal(0, st.Total);
        Assert.All(st.ByMonth, m => Assert.Equal(0, m.Count));
        Assert.Null(st.FirstRegistered);
    }
}
