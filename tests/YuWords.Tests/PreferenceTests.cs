using YuWords.Core;

namespace YuWords.Tests;

/// <summary>登録・除外した語の傾向によるおすすめ順の補正。例はすべて架空</summary>
public sealed class PreferenceTests
{
    static IEnumerable<string> Many(string prefix, int n) => Enumerable.Range(0, n).Select(i => $"{prefix}{(char)('亜' + i)}");

    [Fact]
    public void 語の種類は字の種類と長さで分ける()
    {
        Assert.Equal("漢字・2字まで", Preference.Feature("工符"));
        Assert.Equal("カタカナ・5〜7字", Preference.Feature("リプレース"));
        Assert.Equal("英字＋日本語・5〜7字", Preference.Feature("XYZ設検課"));
        Assert.Equal("英字・3〜4字", Preference.Feature("MOP"));
        Assert.Equal("漢字＋カタカナ・5〜7字", Preference.Feature("共有フォルダ"));
    }

    [Fact]
    public void 登録が多い種類を上げ除外が多い種類を下げる()
    {
        // 漢字2字の語はよく登録し、カタカナの語はよく除外する人
        var registered = Many("工", 20).Concat(["リプレー", "カクメル"]);
        var excluded = Enumerable.Range(0, 15).Select(i => "カタカナ" + (char)('ア' + i)).Concat(Many("図", 3));
        var p = Preference.Learn(registered, excluded);
        Assert.True(p.Factor("桐電") > 1.0);
        Assert.True(p.Factor("カタカナゴ") < 1.0);
        Assert.Equal(1.0, p.Factor("ABCDEFGHIJ"));   // 学んでいない種類は補正しない
    }

    [Fact]
    public void 数が少ないうちは補正しない()
    {
        var p = Preference.Learn(Many("工", 10), Many("図", 5));
        Assert.Equal(1.0, p.Factor("桐電"));
        Assert.Equal(15, p.Samples);
    }

    [Fact]
    public void 補正はおすすめ度に掛かる()
    {
        var item = new ReviewItem(new Candidate { Word = "桐電", Reading = "きりでん", Category = "複合語", MailCount = 7 }, false);
        var before = item.Score;
        item.Affinity = 1.5;
        Assert.Equal(before * 1.5, item.Score, 6);
    }
}
