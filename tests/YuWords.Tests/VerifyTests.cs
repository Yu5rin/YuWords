using YuWords.Core;

namespace YuWords.Tests;

public sealed class VerifyTests
{
    static ImeWorker.Word W(string r, string t) => new(r, t, "名詞");

    [Fact]
    public void 辞書に入っていなかった語は失敗に回す()
    {
        var res = new ImeWorker.Response { Ok = true, Registered = 2, Skipped = 1 };
        res.Succeeded.AddRange([W("こうふ", "工符"), W("ずしめ", "図締"), W("けんりょう", "検了")]);
        ImeWorker.ApplyVerification(res, [("工符", "こうふ"), ("検了", "けんりょう"), ("ほかの語", "ほか")]);
        Assert.True(res.Verified);
        Assert.Equal(["工符", "検了"], res.Succeeded.Select(w => w.Text));
        Assert.Equal(["図締"], res.NotFound.Select(w => w.Text));
        Assert.Contains(res.Failed, w => w.Text == "図締");
        Assert.Equal((1, 1), (res.Registered, res.Skipped));
    }

    [Fact]
    public void 辞書を読み直せなければ確かめない()
    {
        var res = new ImeWorker.Response { Ok = true, Registered = 1 };
        res.Succeeded.Add(W("こうふ", "工符"));
        ImeWorker.ApplyVerification(res, []);
        Assert.False(res.Verified);
        Assert.Single(res.Succeeded);
    }

    [Fact]
    public void 一語も入らなければ効いていないとみなす()
    {
        var r = new RegistrationReport();
        r.Failed.Add(("こうふ", "工符", "名詞"));
        Assert.True(r.LooksIneffective);
        r.Skipped = 1;
        Assert.False(r.LooksIneffective);
    }
}
