using System.Text;
using YuWords.Core;

namespace YuWords.Tests;

public sealed class GoogleImeTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_google_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Fact]
    public void 登録済みの記録は登録先ごとに分ける()
    {
        var s = new AppStore(dir);
        Assert.Equal(1, s.AddRegistered([("工符", "こうふ", "名詞")], GoogleIme.Target));
        Assert.False(s.IsRegistered("工符", "こうふ"));                    // Microsoft IME にはまだ
        Assert.True(s.IsRegisteredIn(GoogleIme.Target, "工符", "こうふ"));
        Assert.Equal(1, s.AddRegistered([("工符", "こうふ", "名詞")]));      // Microsoft IME にも登録できる
        Assert.True(s.IsRegistered("工符", "こうふ"));
        Assert.Equal(0, s.AddRegistered([("工符", "こうふ", "名詞")], GoogleIme.Target));

        var again = new AppStore(dir);
        Assert.Equal(2, again.Registered.Count);
        Assert.Equal(["Google", "MSIME"], again.Registered.Select(r => r.Target).Order());
        // 持ち出し用辞書には同じ語を1回だけ入れる
        var backup = File.ReadAllText(again.BackupFile, Encoding.Unicode);
        Assert.Equal(1, backup.Split("こうふ\t工符").Length - 1);
    }

    [Fact]
    public void 古い登録済みの記録は_Microsoft_IME_として読む()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "registered.json"),
            """[{"Word":"図締","Reading":"ずしめ","Pos":"名詞","RegisteredAt":"2026-10-01T09:00:00"}]""");
        var s = new AppStore(dir);
        Assert.Equal("MSIME", s.Registered.Single().Target);
        Assert.True(s.IsRegistered("図締", "ずしめ"));
    }

    [Fact]
    public void Google形式のファイルを書き出す()
    {
        var (path, n) = GoogleIme.Export(dir, [("こうふ", "工符", "名詞"), ("", "鍛推", "名詞")]);
        Assert.Equal(1, n);
        Assert.StartsWith(Path.Combine(dir, "google_import"), path);
        Assert.Equal("こうふ\t工符\t名詞\t\r\n", File.ReadAllText(path, new UTF8Encoding(false)));
    }

    [Fact]
    public void 辞書ツールの場所()
    {
        if (GoogleIme.ToolPath() is { } p) Assert.EndsWith("GoogleIMEJaTool.exe", p);
    }
}
