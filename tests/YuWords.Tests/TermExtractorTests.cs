using YuWords.Core;

namespace YuWords.Tests;

/// <summary>仕様書 7章のテストケース（参照実装で確認済みの期待値）</summary>
public sealed class TermExtractorTests : IClassFixture<TermExtractorTests.TokenizerFixture>
{
    public sealed class TokenizerFixture : IDisposable
    {
        public MorphTokenizer Tokenizer { get; } = new();
        public void Dispose() => Tokenizer.Dispose();
    }

    readonly TermExtractor ex;

    public TermExtractorTests(TokenizerFixture f) => ex = new TermExtractor(f.Tokenizer, new ExtractorSettings());

    /// <summary>集計で残る語（一般語を除く）を「語|よみ|要確認」の形で返す</summary>
    string[] Candidates(string text) =>
        ex.Extract(text).Select(x => x.Term).Where(t => !t.IsCommon && t.Parent == null)
          .Select(t => $"{t.Word}|{t.Reading}|{(t.Unsure ? "要確認" : "")}").ToArray();

    [Theory]
    [InlineData("ＸＹＺ設検課の山田です", "XYZ設検課|えっくすわいぜっとせつけんか|要確認", "設検課|せつけんか|要確認", "山田|やまだ|要確認")]
    [InlineData("品質保証課と打合せ", "品質保証課|ひんしつほしょうか|")]
    [InlineData("生産管理部へ連絡", "生産管理部|せいさんかんりぶ|")]
    [InlineData("3台出荷します")]
    [InlineData("通り回答いたします。以下案件について")]
    [InlineData("高田様", "高田|たかだ|要確認")]
    [InlineData("ﾊﾟｲﾌﾟｸﾘｯﾌﾟ全数", "パイプクリップ全数|ぱいぷくりっぷぜんすう|")]
    [InlineData("出荷（そのまま）")]
    [InlineData("いつもお世話になっております")]
    [InlineData("Ｂ棟の工符", "B棟|びーむね|要確認", "工符|こうふ|要確認")]
    [InlineData("シ゛ユウタクの件", "ジユウタク|じゆうたく|要確認")]
    public void 仕様書の期待値どおりに抽出する(string text, params string[] expected)
    {
        Assert.Equal(expected, Candidates(text));
    }

    [Fact]
    public void 単独の漢字が記号扱いされた略語もつなげる()
    {
        // 参照実装（Sudachi）では よみ空欄だが、UniDic は字ごとの仮名形を持つ。どちらも要確認
        var words = Candidates("XYZ鍛推に連絡").Select(s => s.Split('|')[0]).ToArray();
        Assert.Equal(["XYZ鍛推", "鍛推"], words);
        Assert.All(Candidates("XYZ鍛推に連絡"), s => Assert.EndsWith("要確認", s));
    }

    [Theory]
    [InlineData("工符")]
    [InlineData("図締変更依頼")]
    [InlineData("検了")]
    [InlineData("桐電")]
    [InlineData("構盤係")]
    [InlineData("盤検課")]
    [InlineData("Eブロック")]
    [InlineData("炉生産中日程判定")]
    public void 社内略語のような語は要確認側に入る(string word)
    {
        var t = ex.Extract($"{word}の件、よろしくお願いします").Select(x => x.Term).First(t => t.Word == word);
        Assert.True(t.Unsure);
        Assert.False(t.IsCommon);
    }

    [Fact]
    public void 句点の直後の未知の英字も名詞として扱う()
    {
        Assert.Contains("XYZ鍛推", Candidates("確認をお願いします。XYZ鍛推と").Select(s => s.Split('|')[0]));
    }

    [Fact]
    public void 空白で複合語を区切る()
    {
        Assert.Equal(["生産管理部|せいさんかんりぶ|", "山田|やまだ|要確認"], Candidates("生産管理部　山田"));
    }

    [Fact]
    public void 長すぎる複合語は除外する()
    {
        Assert.Empty(Candidates("品質保証課生産管理部標準作業手順書改訂版"));
    }
}
