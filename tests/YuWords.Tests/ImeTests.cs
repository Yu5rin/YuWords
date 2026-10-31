using System.Text;
using YuWords.Core;

namespace YuWords.Tests;

/// <summary>IME 連携のうち、実機の IME に触れずに確かめられる部分</summary>
public sealed class ImeTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_ime_{Guid.NewGuid():N}");

    public ImeTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void IMEWRDはmsime_hと同じ大きさ()
    {
        // pwchReading, pwchDisplay, (nPos1,nPos2), rgulAttrs[2], cbComment, uct, pvComment。詰める（msime.h の pack(1)）と詰めない場合
        Assert.Equal(Environment.Is64BitProcess ? 44 : 32, ImeDictionary.PackedWordSize);
        Assert.Equal(Environment.Is64BitProcess ? 48 : 32, ImeDictionary.NaturalWordSize);
    }

    [Fact]
    public void 前回IMEの辞書を使っている最中に落ちたことが分かる()
    {
        var marker = Path.Combine(dir, "ime_dictionary_in_use");
        ImeDictionary.CrashMarkerPath = marker;
        try
        {
            Assert.False(ImeDictionary.CrashedLastTime());
            File.WriteAllText(marker, "");             // 落ちて印が残った
            Assert.True(ImeDictionary.CrashedLastTime());
            Assert.False(File.Exists(marker));          // 一度知らせたら消す
        }
        finally { ImeDictionary.CrashMarkerPath = null; }
    }

    [Fact]
    public void 書き出したIME取り込み形式を読み直せる()
    {
        var path = Path.Combine(dir, "a.txt");
        OutputWriters.WriteImeText(path, [("ひんしつほしょうか", "品質保証課", "名詞"), ("たかだ", "高田", "人名")]);
        Assert.Equal([("ひんしつほしょうか", "品質保証課", "名詞"), ("たかだ", "高田", "人名")], OutputWriters.ReadImeText(path));
    }

    [Fact]
    public void BOM無しUTF8や品詞なしの行も読める()
    {
        var path = Path.Combine(dir, "b.txt");
        File.WriteAllText(path, "!Microsoft IME Dictionary Tool\r\n!コメント\r\nこうふ\t工符\r\n\r\n壊れた行\r\nずしめ\t図締\t名詞\r\n", new UTF8Encoding(false));
        Assert.Equal([("こうふ", "工符", "名詞"), ("ずしめ", "図締", "名詞")], OutputWriters.ReadImeText(path));
    }

    [Fact]
    public void 登録済みから外すと持ち出し用辞書からも消える()
    {
        var s = new AppStore(dir);
        s.AddRegistered([("工符", "こうふ", "名詞"), ("図締", "ずしめ", "名詞")]);
        s.RemoveRegistered(s.Registered.Where(r => r.Word == "工符").ToList());
        Assert.Equal(["図締"], new AppStore(dir).Registered.Select(r => r.Word));
        Assert.DoesNotContain("工符", File.ReadAllText(s.BackupFile, Encoding.Unicode));
    }

    [Fact]
    public void 持ち出し用辞書は変わるたびに前の版を残す()
    {
        var s = new AppStore(dir);
        s.AddRegistered([("工符", "こうふ", "名詞")]);
        Assert.False(Directory.Exists(s.BackupHistoryDir));        // 初回は前の版が無い
        s.WriteBackup();
        Assert.False(Directory.Exists(s.BackupHistoryDir));        // 中身が同じなら残さない
        for (int i = 0; i < AppStore.BackupGenerations + 3; i++)
            s.AddRegistered([($"語{i}", $"ご{i}", "名詞")]);
        var olds = Directory.GetFiles(s.BackupHistoryDir);
        Assert.Equal(AppStore.BackupGenerations, olds.Length);
        Assert.Contains("工符", File.ReadAllText(olds.Order().First(), Encoding.Unicode));
        Assert.DoesNotContain(Directory.GetFiles(dir), f => f.EndsWith(".new"));
    }

    [Fact]
    public void 登録済みの語をCSVで書き出して読み直せる()
    {
        var s = new AppStore(dir);
        s.AddRegistered([("品質保証課", "ひんしつほしょうか", "名詞"), ("高田", "たかだ", "人名")]);
        var path = Path.Combine(dir, "r.csv");
        OutputWriters.WriteRegisteredCsv(path, s.Registered);
        Assert.Equal([("ひんしつほしょうか", "品質保証課", "名詞"), ("たかだ", "高田", "人名")], OutputWriters.ReadWords(path));
    }

    [Fact]
    public void 見出しの無いCSVや列の順番が違うCSVも読める()
    {
        var a = Path.Combine(dir, "a.csv");
        File.WriteAllText(a, "工符,コウフ\r\n図締,ずしめ,固有名詞\r\n,から\r\n");
        Assert.Equal([("こうふ", "工符", "名詞"), ("ずしめ", "図締", "固有名詞")], OutputWriters.ReadWords(a));
        var b = Path.Combine(dir, "b.csv");
        File.WriteAllText(b, "品詞,よみ,語句\r\n人名,たかだ,高田\r\n");
        Assert.Equal([("たかだ", "高田", "人名")], OutputWriters.ReadWords(b));
    }

    [Fact]
    public void ユーザー辞書ツールの場所()
    {
        var path = ImeDictionary.DictionaryToolPath();
        if (path != null) Assert.EndsWith("IMJPDCT.EXE", path, StringComparison.OrdinalIgnoreCase);
    }
}
