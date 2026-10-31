using System.Text;
using YuWords.Core;

namespace YuWords.Tests;

public sealed class ReviewTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_test_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    static Candidate C(string word, string reading, bool unsure = false, int count = 5, bool sig = false, string ime = "") =>
        new() { Word = word, Reading = reading, Unsure = unsure, MailCount = count, Category = "複合語", FromSignature = sig, ImeConversion = ime };

    [Fact]
    public void 登録の初期値は要確認でなくよみがあり未登録ならON()
    {
        Assert.True(new ReviewItem(C("品質保証課", "ひんしつほしょうか"), false).Register);
        Assert.False(new ReviewItem(C("工符", "こうふ", unsure: true), false).Register);
        Assert.False(new ReviewItem(C("鍛推", ""), false).Register);
        Assert.False(new ReviewItem(C("品質保証課", "ひんしつほしょうか"), registered: true).Register);
    }

    [Fact]
    public void よみを直すと要確認が外れ登録がONになる()
    {
        var i = new ReviewItem(C("桐電", "きりでん", unsure: true), false);
        i.EditReading(" ヒサデン ");
        Assert.Equal("ひさでん", i.Reading);
        Assert.False(i.Unsure);
        Assert.True(i.Register);
        Assert.Equal("きりでん", i.Source.Reading);   // 元の推測は残す
    }

    [Fact]
    public void よみを変えずに確定しても要確認は外れない()
    {
        var i = new ReviewItem(C("桐電", "きりでん", unsure: true), false);
        i.EditReading("きりでん");
        Assert.True(i.Unsure);
        Assert.False(i.Register);
    }

    [Fact]
    public void 追加読み込みでは前の選択を引き継ぐ()
    {
        var old = new ReviewItem(C("桐電", "きりでん", unsure: true), false);
        old.EditReading("ひさでん");
        old.Pos = "固有名詞";
        old.Word = "桐電";
        var untouched = new ReviewItem(C("品質保証課", "ひんしつほしょうか"), false) { Register = false };

        var a = new ReviewItem(C("桐電", "きりでん", unsure: true, count: 20), false);
        a.CopyChoicesFrom(old);
        Assert.Equal(("ひさでん", "固有名詞", true, false, 20), (a.Reading, a.Pos, a.Register, a.Unsure, a.MailCount));

        var b = new ReviewItem(C("品質保証課", "ひんしつほしょうか"), false);
        b.CopyChoicesFrom(untouched);
        Assert.False(b.Register);   // 外したチェックは外したまま
    }

    [Fact]
    public void 絞り込み条件()
    {
        var items = new[]
        {
            new ReviewItem(C("品質保証課", "ひんしつほしょうか", ime: "品質保証課"), false),
            new ReviewItem(C("工符", "こうふ", unsure: true, ime: "工符"), false),
            new ReviewItem(C("XYZ設検課", "えっくすわいぜっとせつけんか", unsure: true, sig: true), false),
            new ReviewItem(C("生産管理部", "せいさんかんりぶ"), registered: true),
        };
        string[] Run(ReviewFilter f) => items.Where(f.Matches).Select(i => i.Word).ToArray();

        Assert.Equal(["工符", "XYZ設検課"], Run(new() { UnsureOnly = true }));
        Assert.Equal(["XYZ設検課"], Run(new() { SignatureOnly = true }));
        Assert.Equal(["XYZ設検課", "生産管理部"], Run(new() { NotConvertibleOnly = true }));
        Assert.Equal(["品質保証課"], Run(new() { CheckedOnly = true }));
        Assert.Equal(["品質保証課", "工符", "XYZ設検課"], Run(new() { ShowRegistered = false }));
        Assert.Equal(["XYZ設検課"], Run(new() { Search = "xyz" }));
        Assert.Equal(["工符"], Run(new() { Search = "こう" }));
    }

    [Fact]
    public void 設定と除外リストを保存して読み直せる()
    {
        var s = new AppStore(dir);
        s.Settings.Extractor.Days = 90;
        s.Settings.Extractor.ExtraStopWords = ["定例"];
        s.SaveSettings();
        s.AddExcluded(["山田", "高田"]);

        var s2 = new AppStore(dir);
        Assert.Equal(90, s2.Settings.Extractor.Days);
        Assert.Equal(["定例"], s2.Settings.Extractor.ExtraStopWords);
        Assert.True(s2.Excluded.SetEquals(["山田", "高田"]));
        Assert.Contains("\"定例\"", File.ReadAllText(Path.Combine(dir, "settings.json")));   // 日本語のまま保存
    }

    [Fact]
    public void 登録済みリストは重複させず持ち出し用辞書を作り直す()
    {
        var s = new AppStore(dir);
        Assert.Equal(2, s.AddRegistered([("品質保証課", "ひんしつほしょうか", "名詞"), ("高田", "たかだ", "人名"), ("鍛推", "", "名詞")]));
        Assert.Equal(1, s.AddRegistered([("品質保証課", "ひんしつほしょうか", "名詞"), ("工符", "こうふ", "名詞")]));

        var s2 = new AppStore(dir);
        Assert.Equal(3, s2.Registered.Count);
        Assert.True(s2.IsRegistered("高田", "たかだ"));
        Assert.False(s2.IsRegistered("高田", "こうだ"));

        var text = File.ReadAllText(s2.BackupFile, Encoding.Unicode);
        Assert.Equal("!Microsoft IME Dictionary Tool\r\nひんしつほしょうか\t品質保証課\t名詞\r\nたかだ\t高田\t人名\r\nこうふ\t工符\t名詞\r\n", text);
    }

    [Fact]
    public void 持ち出し用辞書の保存先を変えられる()
    {
        var s = new AppStore(dir);
        s.Settings.BackupPath = Path.Combine(dir, "sub", "backup.txt");
        s.AddRegistered([("工符", "こうふ", "名詞")]);
        Assert.True(File.Exists(Path.Combine(dir, "sub", "backup.txt")));
        Assert.False(File.Exists(Path.Combine(dir, "dictionary_backup.txt")));
    }

    [Fact]
    public void 壊れた設定ファイルは退避して既定値で起動する()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ broken");
        var s = new AppStore(dir);
        Assert.Equal(180, s.Settings.Extractor.Days);
        Assert.True(File.Exists(Path.Combine(dir, "settings.json.broken")));
    }

    [Fact]
    public void 除外リストの語は候補に出さない()
    {
        var mails = Enumerable.Range(0, 4).Select(i => new List<string> { $"品質保証課と生産管理部の件{i}" }).ToList();
        var rows = Extraction.Run(mails, new ExtractorSettings(), new HashSet<string> { "生産管理部" });
        Assert.Contains(rows, c => c.Word == "品質保証課");
        Assert.DoesNotContain(rows, c => c.Word == "生産管理部");
    }
}
