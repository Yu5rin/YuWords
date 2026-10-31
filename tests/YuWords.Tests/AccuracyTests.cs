using YuWords.Core;

namespace YuWords.Tests;

/// <summary>
/// 架空のメール（Data/fictional_mails.txt）で、抽出の精度を測る。
/// 社内用語（架空）を取りこぼさず、日付・数字・普通の語を候補に出さないこと
/// </summary>
public sealed class AccuracyTests
{
    static List<Candidate> Extract()
    {
        var mails = MailTextSource.Read(Path.Combine(AppContext.BaseDirectory, "Data", "fictional_mails.txt"));
        using var tok = new MorphTokenizer();
        var settings = new ExtractorSettings();
        return CandidateBuilder.Build(mails, new TermExtractor(tok, settings), settings);
    }

    [Fact]
    public void 社内用語を取りこぼさない()
    {
        var words = Extract().Where(c => c.Category != CandidateBuilder.PhraseCategory).Select(c => c.Word).ToHashSet();
        string[] expected = ["XYZ設検課", "桐電盤", "工符", "図締", "盤検", "検了"];
        var missing = expected.Where(w => !words.Contains(w)).ToList();
        Assert.True(missing.Count == 0, $"取りこぼした語: {string.Join("、", missing)}");
    }

    [Fact]
    public void 日付や数字や普通の語を候補に出さない()
    {
        var words = Extract().Where(c => c.Category != CandidateBuilder.PhraseCategory).Select(c => c.Word).ToList();
        string[] noise = ["11月10日", "12月1日", "13:30", "内線1234", "Rev.3", "打合せ", "資料", "予定", "確認", "日程", "立会い", "来週", "来週中", "是正済み", "ページ目"];
        var found = noise.Where(words.Contains).ToList();
        Assert.True(found.Count == 0, $"出してしまった語: {string.Join("、", found)}");
        Assert.DoesNotContain(words, w => w.All(ch => char.IsDigit(ch) || "年月日時分:/.-（）()".Contains(ch)));
    }

    [Fact]
    public void 漢字1文字ずつの略語は途中の接頭辞でも切らない()
    {
        var c = Extract().Single(x => x.Word == "桐電盤");
        Assert.Equal("きりでんばん", c.Reading);
        Assert.True(c.Unsure);   // 漢字1文字ずつのよみは推測なので要確認
        // 普通の語の前の接頭辞（新工場）はつながない
        using var tok = new MorphTokenizer();
        var terms = new TermExtractor(tok, new ExtractorSettings()).Extract("本社新工場の件").Select(t => t.Term.Word).ToList();
        Assert.DoesNotContain("本社新工場", terms);
    }

    [Fact]
    public void どの語にも付く接尾辞は外す()
    {
        using var tok = new MorphTokenizer();
        var terms = new TermExtractor(tok, new ExtractorSettings())
            .Extract("来週中に桐電盤の是正済みの3ページ目を図締中に直す").Select(t => t.Term.Word).ToList();
        Assert.DoesNotContain("来週中", terms);
        Assert.DoesNotContain("是正済み", terms);
        Assert.DoesNotContain("ページ目", terms);
        Assert.Contains("図締", terms);   // 外したあとの語は残る
        // 組織の接尾辞は外さない
        Assert.Contains("設検課", new TermExtractor(tok, new ExtractorSettings()).Extract("設検課に送る").Select(t => t.Term.Word));
    }
}
