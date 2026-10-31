using YuWords.Core;

namespace YuWords.Tests;

public sealed class NameReadingTests
{
    [Fact]
    public void 連絡先のふりがなから姓と名と姓名を作る()
    {
        var map = new NameReadings.Map();
        NameReadings.Add(map, "東海林", "トウカイリン", "太郎", "たろう");
        NameReadings.Add(map, "小鳥遊", "", "花子", "ハナコ");          // 姓のふりがなが無い
        NameReadings.Add(map, "ＴＡＮＡＫＡ", "たなか", null, null);   // 全角英字は正規化する
        string? Get(string w) => map.TryGetValue(w, out var r) ? r : null;
        Assert.Equal("とうかいりん", Get("東海林"));
        Assert.Equal("たろう", Get("太郎"));
        Assert.Equal("とうかいりんたろう", Get("東海林太郎"));
        Assert.Null(Get("小鳥遊"));
        Assert.Equal("はなこ", Get("花子"));
        Assert.Null(Get("小鳥遊花子"));
        Assert.Equal("たなか", Get("TANAKA"));
    }

    [Fact]
    public void よみが2通りある名前は使わない()
    {
        var map = new NameReadings.Map();
        NameReadings.Add(map, "山田", "やまだ", "太郎", "たろう");
        NameReadings.Add(map, "山田", "やまた", "花子", "はなこ");
        NameReadings.Add(map, "山田", "やまだ", "次郎", "じろう");   // 一度決められなくなったら戻さない
        Assert.False(map.TryGetValue("山田", out _));
        Assert.True(map.TryGetValue("山田太郎", out var r) && r == "やまだたろう");

        var other = new NameReadings.Map();
        NameReadings.Add(other, "高田", "たかだ", null, null);
        NameReadings.Add(other, "太郎", "たろう", null, null);
        map.AddRange(other);
        Assert.True(map.TryGetValue("高田", out _));
    }

    [Fact]
    public void 候補の人名のよみを置き換えて要確認を外す()
    {
        var rows = new List<Candidate>
        {
            new() { Word = "東海林", Reading = "しょうじ", Unsure = true, Category = "人名", MailCount = 9 },
            new() { Word = "工符", Reading = "こうふ", Unsure = true, Category = "複合語", MailCount = 5 },
            new() { Word = "生産管理部", Reading = "せいさんかんりぶ", Category = "複合語", MailCount = 5 },
        };
        Assert.Equal(["東海林", "工符"], NameReadings.Targets(rows));
        var map = new NameReadings.Map();
        map.Add("東海林", "とうかいりん");
        Assert.Equal(1, NameReadings.Apply(rows, map));
        Assert.Equal("とうかいりん", rows[0].Reading);
        Assert.False(rows[0].Unsure);
        Assert.Equal("連絡先", rows[0].ReadingSource);
        Assert.Equal(9, rows[0].MailCount);
        Assert.True(rows[1].Unsure);
        Assert.True(new ReviewItem(rows[0], false).Register);
    }
}
