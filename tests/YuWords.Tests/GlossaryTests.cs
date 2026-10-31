using YuWords.Core;

namespace YuWords.Tests;

/// <summary>用語集（1行に語句とよみ）の取り込み。例はすべて架空</summary>
public sealed class GlossaryTests
{
    static DocumentSources.Sheet Sheet() => new("用語集",
    [
        ["", "", "", "", "18-4-13 作成"],
        ["＜＜架空用語集＞＞"],
        ["", "※本用語集は日常業務の一助となるよう作成しました"],
        ["No.", "分野", "用語", "読み方", "意味/定義"],
        ["1", "BM", "リプレース", "リプレース", "取替"],
        ["2", "BM", "拡メル", "カクメル", "拡大メル…の略"],
        ["3", "設計", "ＸＹＺ設検課", "エックスワイゼット　セツケンカ", ""],
        ["4", "設計", "工符", "", "読み方が空"],
        ["5", "設計", "図締\n（ずしめ）", "ズシメ", "2行目は使わない"],
        ["6", "設計", "拡メル", "カクメル", "重複"],
        ["7", "", "", "", "語句が空"],
        ["8", "設計", "桐電", "Kiriden", "よみが仮名でない"],
        ["9", "設計", "盤検(盤の検査)", "バンケン", "括弧書きは外す"],
        ["10", "設計", "ＳＰＩ活動 （Software Process Improvement）", "エスピーアイカツドウ", "全角の括弧も"],
        ["11", "設計", "(注記だけ)", "", "外すと空になるならそのまま"],
        ["12", "保守", "MOP 遠隔発報装置", "モップ", "遠くから知らせる"],
        ["13", "保守", "FC P/L", "エフシーピーエル", "説明が英字なら分けない"],
        ["14", "保守", "RM", "アールエム", "英字読みと同じなら別のよみは付けない"],
    ]);

    [Fact]
    public void 見出しの行と列を見つける()
    {
        var s = Sheet();
        int header = DocumentSources.FindHeaderRow(s);
        Assert.Equal(3, header);
        Assert.Equal((2, 3), DocumentSources.GuessGlossaryColumns(s, header));
        Assert.Equal(4, DocumentSources.GuessNoteColumn(s, header));
        Assert.Equal(-1, DocumentSources.FindHeaderRow(new DocumentSources.Sheet("x", [["a", "b"], ["c", "d"]])));
    }

    [Fact]
    public void 語句とよみの組を取り出す()
    {
        var e = DocumentSources.GlossaryEntries(Sheet(), 2, 3, 3).Select(x => (x.Word, x.Reading)).ToList();
        Assert.Equal(
        [
            ("リプレース", "りぷれーす"), ("拡メル", "かくめる"), ("XYZ設検課", "えっくすわいぜっとせつけんか"),
            ("工符", ""), ("図締", "ずしめ"), ("桐電", ""), ("盤検", "ばんけん"), ("SPI活動", "えすぴーあいかつどう"),
            ("(注記だけ)", ""), ("MOP", "もっぷ"), ("FC P/L", "えふしーぴーえる"), ("RM", "あーるえむ"),
        ], e);
    }

    [Fact]
    public void 意味の列を例文として持つ()
    {
        var e = DocumentSources.GlossaryEntries(Sheet(), 2, 3, 3, noteColumn: 4);
        Assert.Equal("取替", e.Single(x => x.Word == "リプレース").Note);
        Assert.Equal("", e.Single(x => x.Word == "XYZ設検課").Note);
    }

    [Fact]
    public void 英字の略語と日本語の説明が続くときは語句を英字だけにする()
    {
        var e = DocumentSources.GlossaryEntries(Sheet(), 2, 3, 3, noteColumn: 4);
        var mop = e.Single(x => x.Word == "MOP");
        Assert.True(mop.Adjusted);
        Assert.Equal("遠隔発報装置。遠くから知らせる", mop.Note);
        Assert.False(e.Single(x => x.Word == "FC P/L").Adjusted);   // 説明が英字なら分けない
    }

    [Fact]
    public void 英字の語でよみが英字読みと違えば英字読みも付ける()
    {
        var e = DocumentSources.GlossaryEntries(Sheet(), 2, 3, 3);
        Assert.Equal("えむおーぴー", e.Single(x => x.Word == "MOP").Extra);
        Assert.Equal("", e.Single(x => x.Word == "RM").Extra);      // 英字読みと同じ
        Assert.Equal("", e.Single(x => x.Word == "拡メル").Extra);  // 英字だけの語ではない

        var item = new ReviewItem(new Candidate { Word = "MOP", Reading = "もっぷ", Category = "用語集", ExtraReadings = ["えむおーぴー"] }, false);
        Assert.Equal([("もっぷ", "MOP", "名詞"), ("えむおーぴー", "MOP", "名詞")], item.Entries());
    }

    [Fact]
    public void よみの列が無くても語句だけ取り出す()
    {
        var e = DocumentSources.GlossaryEntries(Sheet(), 2, -1, 3);
        Assert.Equal(12, e.Count);
        Assert.All(e, x => Assert.Equal("", x.Reading));
    }
}
