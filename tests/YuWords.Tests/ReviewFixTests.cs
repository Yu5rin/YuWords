using YuWords.Core;

namespace YuWords.Tests;

/// <summary>コードの総点検で見つかった不具合の再発防止</summary>
public sealed class ReviewFixTests : IClassFixture<TermExtractorTests.TokenizerFixture>, IDisposable
{
    readonly MorphTokenizer tok;
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_fix_{Guid.NewGuid():N}");

    public ReviewFixTests(TermExtractorTests.TokenizerFixture f) => tok = f.Tokenizer;

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    static ReviewItem Item(string w, string r, bool unsure = false, string ime = "") =>
        new(new Candidate { Word = w, Reading = r, Unsure = unsure, Category = "複合語", ImeConversion = ime }, false);

    [Fact]
    public void IMEの辞書にあることは途中保存を挟んでも残る()
    {
        var a = Item("工符", "こうふ");
        a.InImeDictionary = true;
        var s = ReviewSession.From([a]);
        s.Save(Directory.CreateDirectory(dir).FullName);
        var b = ReviewSession.Load(dir)!.ToItems((_, _) => false).Single();
        Assert.True(b.InImeDictionary);
        Assert.True(b.IsRegistered);
    }

    [Fact]
    public void よみを変えたらIMEの辞書にある扱いではなくなる()
    {
        var a = Item("工符", "こうふ");
        a.InImeDictionary = true;
        Assert.True(a.ComputeRegistered((_, _) => false));
        a.EditReading("こうふごう");
        Assert.False(a.ComputeRegistered((_, _) => false));
        Assert.True(a.ComputeRegistered((w, r) => r == "こうふごう"));   // 登録済みリストにあればそちらで
    }

    [Fact]
    public void 保留の選択を一覧に合わせて保存する()
    {
        var store = new AppStore(dir);
        var a = Item("桐電", "きりでん", unsure: true);
        a.Held = true;
        store.SyncHeld([a]);
        a.EditReading("ひさでん");                 // 保留のまま、よみを直した
        store.SyncHeld([a]);
        Assert.Equal("ひさでん", new AppStore(dir).Held["桐電"].State.Reading);
        a.Held = false;                            // 保留を外した（手で追加し直した・登録した など）
        store.SyncHeld([a]);
        Assert.Empty(new AppStore(dir).Held);
    }

    [Fact]
    public void 一覧に無い保留はそのまま残す()
    {
        var store = new AppStore(dir);
        var a = Item("検了", "けんりょう");
        a.Held = true;
        store.SyncHeld([a]);
        store.SyncHeld([Item("工符", "こうふ")]);   // 別の一覧
        Assert.True(new AppStore(dir).Held.ContainsKey("検了"));
    }

    [Fact]
    public void 直したよみを引き継ぐときは変換結果も引き継ぐ()
    {
        var old = Item("桐電", "きりでん", unsure: true, ime: "東電");
        old.EditReading("ひさでん");
        old.ImeConversion = "桐電";
        var fresh = Item("桐電", "きりでん", unsure: true, ime: "東電");
        fresh.CopyChoicesFrom(old);
        Assert.Equal(("ひさでん", "桐電"), (fresh.Reading, fresh.ImeConversion));
    }

    [Fact]
    public void CSVの品詞はそろえ見出しに品詞が無ければ使わない()
    {
        Directory.CreateDirectory(dir);
        var a = Path.Combine(dir, "a.csv");
        File.WriteAllText(a, "語句,よみ,品詞\r\n高田,たかだ,固有人名\r\n工符,こうふ,なにか\r\n");
        Assert.Equal([("たかだ", "高田", "人名"), ("こうふ", "工符", "名詞")], OutputWriters.ReadWordCsv(a));
        var b = Path.Combine(dir, "b.csv");
        File.WriteAllText(b, "語句,よみ,メモ\r\n高田,たかだ,人名ではない\r\n");
        Assert.Equal([("たかだ", "高田", "名詞")], OutputWriters.ReadWordCsv(b));
    }

    [Fact]
    public void 長すぎて候補にならない複合語からも部分語を作る()
    {
        var ex = new TermExtractor(tok, new ExtractorSettings { MaxCompoundLength = 8 });
        var terms = ex.Extract("標準コアユニット月次報告書").Select(x => x.Term).ToList();
        Assert.DoesNotContain(terms, t => t.Parent == null && t.Word == "標準コアユニット月次報告書");
        Assert.Contains(terms, t => t.Parent == "標準コアユニット月次報告書" && t.Word == "標準コアユニット");
    }

    [Fact]
    public void 持ち出し用辞書を書けなくても登録済みリストは保存する()
    {
        var store = new AppStore(dir);
        store.Settings.BackupPath = Path.Combine(dir, "folder");
        Directory.CreateDirectory(store.Settings.BackupPath);   // フォルダを指している
        Assert.Equal(1, store.AddRegistered([("工符", "こうふ", "名詞")]));
        Assert.NotNull(store.BackupError);
        Assert.True(new AppStore(dir).IsRegistered("工符", "こうふ"));
    }

    [Fact]
    public void 持ち出し用辞書の保存先を確かめる()
    {
        Directory.CreateDirectory(dir);
        Assert.Null(AppStore.CheckBackupPath(Path.Combine(dir, "sub", "b.txt")));
        Assert.NotNull(AppStore.CheckBackupPath(dir));
    }
}
