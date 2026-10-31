using System.Text;
using YuWords.Core;

namespace YuWords.Tests;

/// <summary>Teams の文字起こし（VTT）・HTML・OneNote の MHT からの読み込み。例はすべて架空</summary>
public sealed class MoreSourcesTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_src_{Guid.NewGuid():N}");

    public MoreSourcesTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void Teamsの文字起こしから話した内容と話した人を取り出す()
    {
        var vtt = """
            WEBVTT

            NOTE 架空の会議

            1f3a9c2e-4b5d-4e6f-8a7b-9c0d1e2f3a4b/12-0
            00:00:01.000 --> 00:00:04.000
            <v 高田 一郎>桐電盤の図締は来週です。</v>

            2
            00:00:05.000 --> 00:00:07.500
            <v 高田 一郎>XYZ設検課に確認します。</v>

            00:00:08.000 --> 00:00:09.000
            <v 中野 花子>工符を送ります &amp; 確認します。</v>
            """;
        var lines = DocumentSources.SplitLines(DocumentSources.StripVtt(vtt));
        Assert.Equal(["高田 一郎", "桐電盤の図締は来週です。", "XYZ設検課に確認します。", "中野 花子", "工符を送ります & 確認します。"], lines);
    }

    [Fact]
    public void HTMLから文字だけを取り出す()
    {
        var html = "<html><head><title>x</title><style>p{color:red}</style></head><body>" +
                   "<div>桐電盤の<b>図締</b></div><p>工符&nbsp;Rev.3</p><script>var a='盤検';</script><table><tr><td>検了</td><td>済</td></tr></table></body></html>";
        var lines = DocumentSources.SplitLines(DocumentSources.HtmlToText(html));
        Assert.Contains("桐電盤の 図締", lines);
        Assert.Contains("工符 Rev.3", lines);
        Assert.DoesNotContain(lines, l => l.Contains("盤検") || l.Contains("color"));
        Assert.Contains(lines, l => l.StartsWith("検了"));
    }

    [Fact]
    public void OneNoteのMHTからHTMLの部分を読む()
    {
        // quoted-printable（UTF-8）の HTML と、無視する画像の部分
        var html = Encoding.UTF8.GetBytes("<p>XYZ設検課の工符</p><p>図締</p>");
        var qp = new StringBuilder();
        foreach (var b in html) qp.Append(b < 128 && b != '=' ? ((char)b).ToString() : $"={b:X2}");
        var qpText = qp.ToString();
        int at = qpText.IndexOf("XYZ", StringComparison.Ordinal);
        var folded = qpText[..at] + "=\r\n" + qpText[at..];   // 行の折り返し（エスケープの途中では折り返さない）
        var mht = "MIME-Version: 1.0\r\nContent-Type: multipart/related;\r\n\tboundary=\"----=_NextPart_01\"\r\n\r\n" +
                  "------=_NextPart_01\r\nContent-Type: text/html; charset=\"utf-8\"\r\nContent-Transfer-Encoding: quoted-printable\r\n\r\n" +
                  folded + "\r\n\r\n" +
                  "------=_NextPart_01\r\nContent-Type: image/png\r\nContent-Transfer-Encoding: base64\r\n\r\niVBORw0KGgo=\r\n\r\n" +
                  "------=_NextPart_01--\r\n";
        var path = Path.Combine(dir, "会議メモ.mht");
        File.WriteAllText(path, mht, Encoding.ASCII);
        var lines = DocumentSources.ReadDocument(path)!;
        Assert.Equal(["会議メモ", "XYZ設検課の工符", "図締"], lines);
    }

    [Fact]
    public void 貼り付けた文章を段落ごとに分ける()
    {
        var docs = DocumentSources.Paragraphs("高田: 桐電盤の件\r\n図締は来週\r\n\r\n\r\n中野: 了解です\n");
        Assert.Equal(2, docs.Count);
        Assert.Equal(["高田: 桐電盤の件", "図締は来週"], docs[0]);
        Assert.Empty(DocumentSources.Paragraphs("  \n "));
    }

    [Fact]
    public void フォルダの読み込みで新しい種類も読む()
    {
        File.WriteAllText(Path.Combine(dir, "定例.vtt"), "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n<v 高田>盤検の件</v>\n");
        File.WriteAllText(Path.Combine(dir, "チャット.html"), "<p>検了しました</p>");
        var docs = DocumentSources.ReadFolder(dir);
        Assert.Contains(docs, d => d.SequenceEqual(["定例", "高田", "盤検の件"]));
        Assert.Contains(docs, d => d.SequenceEqual(["チャット", "検了しました"]));
    }
}
