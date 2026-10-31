using System.Text;
using YuWords.Core;

namespace YuWords.Tests;

public sealed class PipelineTests
{
    [Fact]
    public void 正規化で全角英数と半角カナと分離した濁点を揃える()
    {
        Assert.Equal("XYZ", MailPreprocessor.Normalize("ＸＹＺ"));
        Assert.Equal("パイプクリップ", MailPreprocessor.Normalize("ﾊﾟｲﾌﾟｸﾘｯﾌﾟ"));
        Assert.Equal("ジユウタク", MailPreprocessor.Normalize("シ゛ユウタク"));
    }

    [Theory]
    [InlineData("-----Original Message-----")]
    [InlineData("________________________________")]
    [InlineData("差出人： 山田")]
    [InlineData("From: yamada@example.com")]
    [InlineData("On Mon, Oct 1, 2026 Yamada wrote:")]
    [InlineData("2026/10/01 10:00 山田 書きました")]
    public void 引用の開始行以降を捨てる(string marker)
    {
        var body = $"本文1\n\n> 引用行\n＞全角引用\n  本文2  \n{marker}\n前のメール";
        Assert.Equal(["本文1", "本文2"], MailPreprocessor.StripQuote(body));
    }

    [Fact]
    public void 多くのメールに出る行を署名として分ける()
    {
        var mails = Enumerable.Range(0, 10)
            .Select(i => new List<string> { $"件名{i}", $"本文{i}", "生産管理部 山田" }).ToList();
        var (bodies, common) = MailPreprocessor.SplitCommonLines(mails, 0.1);
        Assert.Equal(10, common["生産管理部 山田"]);
        Assert.Single(common);
        Assert.Equal("件名0\n本文0", bodies[0]);
    }

    [Fact]
    public void 出現メール数で数え署名由来の語に印を付ける()
    {
        var mails = Enumerable.Range(0, 5).Select(i => new List<string>
        {
            $"打合せ{i}", $"品質保証課との打合せの件{i}。品質保証課に再送します", "XYZ設検課 山田",
        }).ToList();
        mails.Add(["品質保証課へ"]);   // 6通目にだけ出る

        using var tok = new MorphTokenizer();
        var ex = new TermExtractor(tok, new ExtractorSettings());
        var rows = CandidateBuilder.Build(mails, ex, new ExtractorSettings());

        var qa = rows.Single(c => c.Word == "品質保証課");
        Assert.Equal(6, qa.MailCount);          // 1通に2回出ても1
        Assert.False(qa.FromSignature);
        Assert.Equal("ひんしつほしょうか", qa.Reading);

        var sig = rows.Single(c => c.Word == "XYZ設検課");
        Assert.True(sig.FromSignature);
        Assert.Equal(5, sig.MailCount);
        Assert.Equal(rows.Select(r => r.MailCount).OrderByDescending(n => n), rows.Select(r => r.MailCount));
    }

    [Fact]
    public void IME取り込み形式はUTF16LEのBOM付きでCRLF()
    {
        var path = Path.GetTempFileName();
        try
        {
            int n = OutputWriters.WriteImeText(path, [("ひんしつほしょうか", "品質保証課", "名詞"), ("", "鍛推", "名詞")]);
            Assert.Equal(1, n);   // よみ空欄は出さない
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xFF, 0xFE }, bytes[..2]);
            Assert.Equal("!Microsoft IME Dictionary Tool\r\nひんしつほしょうか\t品質保証課\t名詞\r\n",
                         Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 開かれているファイルには日時付きの別名で書く()
    {
        var path = Path.Combine(Path.GetTempPath(), $"yuwords_{Guid.NewGuid():N}.csv");
        try
        {
            using (new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var alt = OutputWriters.WritablePath(path);
                Assert.NotEqual(path, alt);
                Assert.Matches(@"_\d{8}_\d{6}\.csv$", alt);
            }
            Assert.Equal(path, OutputWriters.WritablePath(path));
        }
        finally { File.Delete(path); }
    }
}
