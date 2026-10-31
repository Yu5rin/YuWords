using System.Text;
using YuWords.Core;

namespace YuWords.Tests;

public sealed class TeamDictionaryTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_team_{Guid.NewGuid():N}");
    string PathCsv => Path.Combine(dir, "共有", "チームの辞書.csv");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void 足した語を読み直せて重複は足さない()
    {
        Assert.Empty(TeamDictionary.Read(PathCsv));   // まだ無い
        Assert.Equal(2, TeamDictionary.Add(PathCsv, [("工符", "こうふ", "名詞"), ("高田", "たかだ", "人名")], "yamada"));
        Assert.Equal(1, TeamDictionary.Add(PathCsv, [("工符", "こうふ", "名詞"), ("図締", "ずしめ", "名詞"), ("よみなし", "", "名詞")], "tanaka"));
        var words = TeamDictionary.Read(PathCsv);
        Assert.Equal(["工符", "高田", "図締"], words.Select(w => w.Word));
        Assert.Equal(("人名", "yamada"), (words[1].Pos, words[1].AddedBy));
        Assert.Equal("tanaka", words[2].AddedBy);
        // Excel で開けるよう BOM 付き UTF-8・見出しあり
        var bytes = File.ReadAllBytes(PathCsv);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.StartsWith("語句,よみ,品詞,追加した人,追加日", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public void 同時に足しても語が失われない()
    {
        var tasks = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (int i = 0; i < 5; i++)
                TeamDictionary.Add(PathCsv, [($"語{t}-{i}", $"ご{t}の{i}", "名詞")], $"user{t}");
        })).ToArray();
        Task.WaitAll(tasks);
        Assert.Equal(20, TeamDictionary.Read(PathCsv).Count);
    }

    [Fact]
    public void 新しい語は見た語と登録済みと除外を除く()
    {
        var store = new AppStore(Path.Combine(dir, "app"));
        store.AddRegistered([("工符", "こうふ", "名詞")]);
        store.AddExcluded(["除外語"]);
        store.MarkTeamSeen([TeamDictionary.Key("図締", "ずしめ")]);
        var team = new List<TeamWord>
        {
            new("工符", "こうふ", "名詞", "", ""), new("図締", "ずしめ", "名詞", "", ""),
            new("除外語", "じょがいご", "名詞", "", ""), new("検了", "けんりょう", "名詞", "", ""),
        };
        Assert.Equal(["検了"], TeamDictionary.NewWords(team, store).Select(w => w.Word));
        Assert.Contains(TeamDictionary.Key("図締", "ずしめ"), new AppStore(Path.Combine(dir, "app")).TeamSeen);
    }

    [Fact]
    public void 手で作ったCSVも見出しの名前で読む()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathCsv)!);
        File.WriteAllText(PathCsv, "よみ,語句,メモ\r\nコウフ,工符,社内略語\r\n", new UTF8Encoding(true));
        var w = Assert.Single(TeamDictionary.Read(PathCsv));
        Assert.Equal(("工符", "こうふ", "名詞"), (w.Word, w.Reading, w.Pos));
    }
}
