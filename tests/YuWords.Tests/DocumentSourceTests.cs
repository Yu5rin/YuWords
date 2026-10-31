using System.IO.Compression;
using System.Text;
using YuWords.Core;

namespace YuWords.Tests;

public sealed class DocumentSourceTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_src_{Guid.NewGuid():N}");

    public DocumentSourceTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void Markdownの記法を外して本文だけにする()
    {
        var md = """
            ---
            tags: [会議]
            ---
            # 図締変更の打合せ
            - [ ] [[品質保証課]]に確認 #todo
            - [[生産管理部|生管]]へ連絡、詳細は[資料](https://example.com/a)
            > **工符**の件
            ```
            コードブロックの中の語
            ```
            %%非表示のコメント%%
            | 項目 | 担当 |
            |---|---|
            | 検了 | 桐電 |
            """;
        var lines = DocumentSources.SplitLines(DocumentSources.StripMarkdown(md));
        Assert.Equal(["図締変更の打合せ", "品質保証課に確認 todo", "生管へ連絡、詳細は資料", "工符の件", "項目", "担当", "検了", "桐電"], lines);
    }

    [Fact]
    public void フォルダの隠しフォルダは読まずファイル名も本文に含める()
    {
        File.WriteAllText(Path.Combine(dir, "工符一覧.md"), "# 見出し\n本文です");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "sub", "メモ.txt"), "図締の件");
        File.WriteAllText(Path.Combine(dir, "sub", "画像.png"), "x");
        Directory.CreateDirectory(Path.Combine(dir, ".obsidian"));
        File.WriteAllText(Path.Combine(dir, ".obsidian", "設定.md"), "読まない");

        var docs = DocumentSources.ReadFolder(dir);
        Assert.Equal(2, docs.Count);
        Assert.Contains(docs, d => d.SequenceEqual(["工符一覧", "見出し", "本文です"]));
        Assert.Contains(docs, d => d.SequenceEqual(["メモ", "図締の件"]));
    }

    static void Zip(string path, params (string Name, string Xml)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, xml) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            w.Write(xml);
        }
    }

    [Fact]
    public void WordとPowerPointの本文を読める()
    {
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        Zip(Path.Combine(dir, "報告.docx"), ("word/document.xml",
            $"""<w:document xmlns:w="{w}"><w:body><w:p><w:r><w:t>図締変更</w:t></w:r><w:r><w:t>の件</w:t></w:r></w:p><w:p><w:r><w:t>工符</w:t><w:tab/><w:t>検了</w:t></w:r></w:p></w:body></w:document>"""));
        Zip(Path.Combine(dir, "説明.pptx"),
            ("ppt/slides/slide10.xml", $"""<p:sld xmlns:p="x" xmlns:a="{a}"><a:p><a:r><a:t>10枚目</a:t></a:r></a:p></p:sld>"""),
            ("ppt/slides/slide2.xml", $"""<p:sld xmlns:p="x" xmlns:a="{a}"><a:p><a:r><a:t>桐電</a:t></a:r><a:r><a:t>の構盤係</a:t></a:r></a:p></p:sld>"""),
            ("ppt/notesSlides/notesSlide1.xml", $"""<p:notes xmlns:p="x" xmlns:a="{a}"><a:p><a:r><a:t>メモ書き</a:t></a:r></a:p></p:notes>"""));
        File.WriteAllText(Path.Combine(dir, "~$報告.docx"), "一時ファイル");

        var docs = DocumentSources.ReadFolder(dir);
        Assert.Equal(2, docs.Count);
        Assert.Contains(docs, d => d.SequenceEqual(["報告", "図締変更の件", "工符 検了"]));
        Assert.Contains(docs, d => d.SequenceEqual(["説明", "桐電の構盤係", "10枚目", "メモ書き"]));
    }

    [Fact]
    public void 最近更新したファイルだけを読める()
    {
        var oldFile = Path.Combine(dir, "古い.md");
        File.WriteAllText(oldFile, "古い本文");
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-40));
        File.WriteAllText(Path.Combine(dir, "新しい.md"), "新しい本文");
        var docs = DocumentSources.ReadFolder(dir, modifiedAfter: DateTime.Now.AddDays(-30));
        Assert.Equal(["新しい", "新しい本文"], Assert.Single(docs));
    }

    [Fact]
    public void ShiftJISのテキストも読める()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(dir, "sjis.txt");
        File.WriteAllBytes(path, Encoding.GetEncoding(932).GetBytes("品質保証課へ連絡"));
        Assert.Equal("品質保証課へ連絡", DocumentSources.ReadText(path));
    }

    [Fact]
    public void CSVの引用符と改行入りの値を読める()
    {
        var rows = DocumentSources.ParseDelimited("件名,本文\r\n図締,\"工符の件、\"\"至急\"\"\n2行目\"\r\n検了,桐電\r\n", ',');
        Assert.Equal(3, rows.Count);
        Assert.Equal(["図締", "工符の件、\"至急\"\n2行目"], rows[1]);
        Assert.Equal(["検了", "桐電"], rows[2]);
    }

    [Fact]
    public void 指定列のセルを1つずつ文書にする()
    {
        var sheet = new DocumentSources.Sheet("s", [["件名", "本文"], ["a", "工符の件\n図締"], ["b"], ["c", "  "], ["d", "検了"]]);
        var docs = DocumentSources.ColumnDocuments(sheet, 1, skipHeader: true);
        Assert.Equal([["工符の件", "図締"], ["検了"]], docs);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    public void 列名(int index, string name) => Assert.Equal(name, DocumentSources.ColumnName(index));

    [Fact]
    public void Excelファイルを読める()
    {
        var path = Path.Combine(dir, "a.xlsx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Put(string name, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(xml);
            }
            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Put("xl/workbook.xml", $"""<workbook xmlns="{ns}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="案件" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Put("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
            Put("xl/sharedStrings.xml", $"""<sst xmlns="{ns}"><si><t>件名</t></si><si><r><t>工符</t></r><r><t>の件</t></r><rPh><t>コウフ</t></rPh></si></sst>""");
            Put("xl/worksheets/sheet1.xml", $"""<worksheet xmlns="{ns}"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c></row><row r="3"><c r="B3" t="s"><v>1</v></c><c r="C3" t="inlineStr"><is><t>図締</t></is></c><c r="D3"><v>42</v></c></row></sheetData></worksheet>""");
        }

        var sheet = Assert.Single(DocumentSources.ReadTable(path));
        Assert.Equal("案件", sheet.Name);
        Assert.Equal(3, sheet.Rows.Count);
        Assert.Equal(["件名"], sheet.Rows[0]);
        Assert.Empty(sheet.Rows[1]);
        Assert.Equal(["", "工符の件", "図締", "42"], sheet.Rows[2]);   // ふりがな（rPh）は含めない
    }
}
