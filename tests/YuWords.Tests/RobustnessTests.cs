using System.Text;
using YuWords.Core;

namespace YuWords.Tests;

/// <summary>壊れたファイル・空の入力など、異常なときに落ちないこと</summary>
public sealed class RobustnessTests : IClassFixture<TermExtractorTests.TokenizerFixture>, IDisposable
{
    readonly MorphTokenizer tok;
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_rob_{Guid.NewGuid():N}");

    public RobustnessTests(TermExtractorTests.TokenizerFixture f)
    {
        tok = f.Tokenizer;
        Directory.CreateDirectory(dir);
    }

    public void Dispose() => Directory.Delete(dir, true);

    [Theory]
    [InlineData("held.json")]
    [InlineData("learned.json")]
    [InlineData("registered.json")]
    [InlineData("excluded.json")]
    public void 壊れた保存ファイルは退避して空で始める(string name)
    {
        File.WriteAllText(Path.Combine(dir, name), "{ 壊れた");
        var s = new AppStore(dir);
        Assert.Empty(s.Held);
        Assert.Empty(s.Learned);
        Assert.Empty(s.Registered);
        Assert.Empty(s.Excluded);
        Assert.True(File.Exists(Path.Combine(dir, name + ".broken")));
    }

    [Fact]
    public void 空のメール群や空行だけのメールでも落ちない()
    {
        var ex = new TermExtractor(tok, new ExtractorSettings());
        Assert.Empty(CandidateBuilder.Build([], ex, new ExtractorSettings()));
        Assert.Empty(CandidateBuilder.Build([[], [""], ["   "]], ex, new ExtractorSettings()));
        Assert.Empty(ex.Extract(""));
        Assert.Empty(ex.Extract("、。！？「」"));
        Assert.Empty(ex.Extract(new string('あ', 20000)));   // 長すぎる行
    }

    [Fact]
    public void 記号や英数字だけの行でも落ちない()
    {
        var ex = new TermExtractor(tok, new ExtractorSettings());
        foreach (var line in new[] { "----", "=====", "http://example.com/a?b=c", "12345", "ABC-123_XYZ", "\t\t", "🙂😀", "ｶﾞｷﾞｸﾞｹﾞｺﾞ" })
            _ = ex.Extract(line);
    }

    [Fact]
    public void 壊れたExcelやWordは読めないことを知らせる()
    {
        var x = Path.Combine(dir, "broken.xlsx");
        File.WriteAllText(x, "zip ではない");
        Assert.ThrowsAny<Exception>(() => DocumentSources.ReadTable(x));

        // フォルダの取り込みでは、壊れたファイルを飛ばして続ける
        File.WriteAllText(Path.Combine(dir, "broken.docx"), "zip ではない");
        File.WriteAllText(Path.Combine(dir, "broken.pptx"), "");
        File.WriteAllText(Path.Combine(dir, "ok.md"), "工符の件");
        var docs = DocumentSources.ReadFolder(dir);
        Assert.Equal(["ok", "工符の件"], Assert.Single(docs));
    }

    [Fact]
    public void 空の辞書ファイルやCSVは0語として読む()
    {
        var empty = Path.Combine(dir, "e.txt");
        File.WriteAllText(empty, "");
        Assert.Empty(DictionaryFormats.Read(empty));
        var csv = Path.Combine(dir, "e.csv");
        File.WriteAllText(csv, "");
        Assert.Empty(DictionaryFormats.Read(csv));
        var header = Path.Combine(dir, "h.csv");
        File.WriteAllText(header, "語句,よみ,品詞\r\n");
        Assert.Empty(DictionaryFormats.Read(header));
    }

    [Fact]
    public void 存在しないフォルダは0件として読む()
    {
        Assert.Empty(DocumentSources.ReadFolder(Path.Combine(dir, "ない")));
    }

    [Fact]
    public void 開けないメールテキストは例外で知らせる()
    {
        Assert.ThrowsAny<IOException>(() => MailTextSource.Read(Path.Combine(dir, "ない.txt")));
    }

    [Fact]
    public void よみのルールに変な行が混ざっても使える行だけ使う()
    {
        var ex = new TermExtractor(tok, new ExtractorSettings { ReadingRules = ["", "=", "係=", "=がかり", "係=がかり", "係=ガカリ"] });
        Assert.Equal("せっけいがかり", ex.Extract("設計係").Select(x => x.Term).First().Reading);
    }

    [Fact]
    public void BOM付きUTF16のIME取り込み形式も読める()
    {
        var p = Path.Combine(dir, "u.txt");
        File.WriteAllText(p, "!Microsoft IME Dictionary Tool\r\nこうふ\t工符\tさ変名詞\r\n", new UnicodeEncoding(false, true));
        Assert.Equal([("こうふ", "工符", "名詞")], DictionaryFormats.Read(p));
    }
}
