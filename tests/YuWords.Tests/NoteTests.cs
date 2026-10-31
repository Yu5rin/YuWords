using YuWords.Core;

namespace YuWords.Tests;

/// <summary>語の意味メモ・用語を引く・チームの辞書の意味。例はすべて架空</summary>
public sealed class NoteTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_note_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void メモを書いて残す()
    {
        var s = new AppStore(dir);
        s.SetNote("MOP", " 遠隔発報装置 ");
        Assert.Equal(1, s.AddNotes([("MOP", "上書きしない"), ("図締", "図面の締め切り"), ("工符", "")]));
        var again = new AppStore(dir);
        Assert.Equal("遠隔発報装置", again.Notes["MOP"]);
        Assert.Equal("図面の締め切り", again.Notes["図締"]);
        Assert.False(again.Notes.ContainsKey("工符"));
        again.SetNote("MOP", "");
        Assert.False(new AppStore(dir).Notes.ContainsKey("MOP"));
    }

    [Fact]
    public void 用語を引く()
    {
        var s = new AppStore(dir);
        s.AddRegistered([("MOP", "もっぷ", "名詞"), ("MOP", "えむおーぴー", "名詞"), ("図締", "ずしめ", "名詞"), ("お世話になっております。", "おせ", "短縮よみ")]);
        s.SetNote("MOP", "遠隔発報装置");
        var team = new[]
        {
            new TeamWord("図締", "ずしめ", "名詞", "tanaka", "2026/10/01", "図面の締め切り"),
            new TeamWord("盤検", "ばんけん", "名詞", "tanaka", "2026/10/01", "盤の検査"),
        };
        var all = TermLookup.Build(s, team);
        Assert.Equal(3, all.Count);   // 定型文は入れない
        var mop = all.Single(e => e.Word == "MOP");
        Assert.Equal(("もっぷ、えむおーぴー", "遠隔発報装置", TermLookup.Mine), (mop.Reading, mop.Note, mop.Source));
        Assert.Equal("図面の締め切り", all.Single(e => e.Word == "図締").Note);   // 自分のメモが無ければチームの意味
        Assert.Equal(TermLookup.Team, all.Single(e => e.Word == "盤検").Source);

        Assert.Equal(["図締"], TermLookup.Search(all, "ズシ").Select(e => e.Word));   // カタカナで打っても
        Assert.Equal(["MOP"], TermLookup.Search(all, "えむ").Select(e => e.Word));     // 別のよみでも
        Assert.Equal(["盤検"], TermLookup.Search(all, "検査").Select(e => e.Word));     // 意味でも
        Assert.Equal(["MOP"], TermLookup.Search(all, "ｍｏ").Select(e => e.Word));     // 全角でも
        Assert.Equal(3, TermLookup.Search(all, " ").Count);
    }

    [Fact]
    public void チームの辞書に意味を載せる()
    {
        var path = Path.Combine(dir, "チームの辞書.csv");
        Directory.CreateDirectory(dir);
        TeamDictionary.Add(path, [("MOP", "もっぷ", "名詞", "遠隔発報装置, 新型"), ("図締", "ずしめ", "名詞", "")], "yamada");
        // 意味が空の語には、あとから足した人の意味を入れる
        Assert.Equal(0, TeamDictionary.Add(path, [("図締", "ずしめ", "名詞", "図面の締め切り"), ("MOP", "もっぷ", "名詞", "上書きしない")], "tanaka"));
        var words = TeamDictionary.Read(path);
        Assert.Equal("遠隔発報装置, 新型", words.Single(w => w.Word == "MOP").Note);
        Assert.Equal("図面の締め切り", words.Single(w => w.Word == "図締").Note);
        Assert.Equal("yamada", words.Single(w => w.Word == "図締").AddedBy);
    }

    [Fact]
    public void 意味の列が無い前の版のチームの辞書も読める()
    {
        var path = Path.Combine(dir, "古い.csv");
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, "語句,よみ,品詞,追加した人,追加日\r\n工符,こうふ,名詞,yamada,2026/09/01\r\n", new System.Text.UTF8Encoding(true));
        var w = TeamDictionary.Read(path).Single();
        Assert.Equal(("工符", ""), (w.Word, w.Note));
        TeamDictionary.Add(path, [("図締", "ずしめ", "名詞", "図面の締め切り")], "tanaka");
        Assert.StartsWith("\uFEFF語句,よみ,品詞,追加した人,追加日,意味", File.ReadAllText(path, new System.Text.UTF8Encoding(false)));
    }
}
