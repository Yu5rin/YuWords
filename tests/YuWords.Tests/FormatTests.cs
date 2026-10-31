using System.Text;
using YuWords.Core;

namespace YuWords.Tests;

public sealed class FormatTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_fmt_{Guid.NewGuid():N}");

    public FormatTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, true);

    static readonly (string, string, string)[] Words =
    [
        ("ひんしつほしょうか", "品質保証課", "名詞"), ("たかだ", "高田", "人名"), ("しょめい", "生産管理部 山田", "短縮よみ"), ("", "鍛推", "名詞"),
    ];

    [Fact]
    public void Google日本語入力の形式()
    {
        var path = Path.Combine(dir, "g.txt");
        Assert.Equal(3, DictionaryFormats.Write(path, Words, DictFormat.Google));
        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]);   // BOM なしの UTF-8
        Assert.Equal("ひんしつほしょうか\t品質保証課\t名詞\t\r\nたかだ\t高田\t人名\t\r\nしょめい\t生産管理部 山田\t短縮よみ\t\r\n",
            Encoding.UTF8.GetString(bytes));
        Assert.Equal(Words[..3], DictionaryFormats.Read(path));
    }

    [Fact]
    public void ATOKの形式()
    {
        var path = Path.Combine(dir, "a.txt");
        DictionaryFormats.Write(path, Words, DictFormat.Atok);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, bytes[..2]);
        Assert.Equal("!!ATOK_TANGO_TEXT_HEADER_1\r\nひんしつほしょうか\t品質保証課\t名詞\r\nたかだ\t高田\t固有人名\r\nしょめい\t生産管理部 山田\t短縮読み\r\n",
            Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));
        Assert.Equal(Words[..3], DictionaryFormats.Read(path));   // ATOK の品詞名も YuWords の名前に戻る
    }

    [Fact]
    public void MicrosoftIMEの形式とCSVも読める()
    {
        var ime = Path.Combine(dir, "m.txt");
        DictionaryFormats.Write(ime, Words, DictFormat.MicrosoftIme);
        Assert.Equal(Words[..3], DictionaryFormats.Read(ime));
        var csv = Path.Combine(dir, "c.csv");
        File.WriteAllText(csv, "語句,よみ,品詞\r\n工符,こうふ,名詞\r\n");
        Assert.Equal([("こうふ", "工符", "名詞")], DictionaryFormats.Read(csv));
    }

    [Theory]
    [InlineData("固有人名", "人名")]
    [InlineData("姓", "人名")]
    [InlineData("固有地名", "地名")]
    [InlineData("組織", "固有名詞")]
    [InlineData("短縮読み", "短縮よみ")]
    [InlineData("さ変名詞", "名詞")]
    public void 品詞名をそろえる(string pos, string expected) => Assert.Equal(expected, DictionaryFormats.NormalizePos(pos));
}
