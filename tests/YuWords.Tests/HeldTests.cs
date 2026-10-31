using YuWords.Core;

namespace YuWords.Tests;

public sealed class HeldTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_held_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    static ReviewItem Item(string w, string r, bool unsure = false) =>
        new(new Candidate { Word = w, Reading = r, Unsure = unsure, Category = "複合語" }, false);

    [Fact]
    public void 保留はよみと品詞を残して保存される()
    {
        var s = new AppStore(dir);
        var a = Item("桐電", "きりでん", unsure: true);
        a.EditReading("ひさでん");
        a.Pos = "固有名詞";
        s.Hold([a]);

        var held = new AppStore(dir).Held["桐電"];
        Assert.True(held.State.Held);
        Assert.Equal(("ひさでん", "固有名詞"), (held.State.Reading, held.State.Pos));

        s.Unhold(["桐電"]);
        Assert.Empty(new AppStore(dir).Held);
    }

    [Fact]
    public void 未確認のみと保留の絞り込み()
    {
        var a = Item("工符", "こうふ");
        var b = Item("図締", "ずしめ");
        b.Reviewed = true;
        var c = Item("検了", "けんりょう");
        c.Held = true;
        var d = Item("生産管理部", "せいさんかんりぶ");
        d.IsRegistered = true;
        var items = new[] { a, b, c, d };
        string[] Run(ReviewFilter f) => items.Where(f.Matches).Select(i => i.Word).ToArray();

        Assert.Equal(["工符", "図締", "生産管理部"], Run(new()));                      // 保留は既定で隠す
        Assert.Equal(["工符", "図締", "検了", "生産管理部"], Run(new() { ShowHeld = true }));
        Assert.Equal(["工符"], Run(new() { UnreviewedOnly = true }));               // 確認済み・登録済み・保留は除く
    }

    [Fact]
    public void 確認済みと保留も控えに入る()
    {
        var a = Item("工符", "こうふ");
        a.Reviewed = true;
        a.Held = true;
        var b = Item("工符", "こうふ");
        b.Restore(a.Save());
        Assert.True(b.Reviewed && b.Held);
    }
}
