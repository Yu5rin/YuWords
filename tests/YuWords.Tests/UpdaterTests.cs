using System.IO.Compression;
using System.Text;
using YuWords.Core;

namespace YuWords.Tests;

[Collection("更新の作業フォルダ")]   // Updater.UpdateDir を共有するので順に動かす
public sealed class UpdaterTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_upd_{Guid.NewGuid():N}");

    public UpdaterTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, true);

    const string Hash = "C90564039723ED12F05A7CF37FBFE21329396303EF34364F6F5557632EC91487";

    static string Release(string tag, bool digest) => $$"""
        {
          "tag_name": "{{tag}}", "draft": false,
          "body": "## 変更点\n\n- 用語集を取り込めるようにした\n- 落ちるのを直した\n\n## ダウンロード\n\n| ファイル | サイズ | SHA256 |\n|---|---|---|\n| YuWords-{{tag[1..]}}-win-x64.zip | 112.3 MB | `{{Hash}}` |\n\nインストール方法は README を見てください。",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.com/notes.txt", "size": 1 },
            { "name": "YuWords-{{tag[1..]}}-win-x64.zip", "browser_download_url": "https://example.com/YuWords.zip", "size": 117755605
              {{(digest ? ", \"digest\": \"sha256:" + Hash.ToLowerInvariant() + "\"" : "")}} }
          ]
        }
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void リリースから新しい版の配布物とSHA256を読む(bool digest)
    {
        var info = Updater.ParseRelease(Release("v0.5.0", digest), new Version(0, 4, 1))!;
        Assert.Equal(new Version(0, 5, 0), info.Version);
        Assert.Equal("https://example.com/YuWords.zip", info.Location);
        Assert.Equal(Hash, info.Sha256);   // digest が無ければ本文の表から
        Assert.Equal("- 用語集を取り込めるようにした\n- 落ちるのを直した", info.Notes);
        Assert.True(info.FromGitHub);
    }

    [Fact]
    public void 版は_Directory_Build_props_でそろえてある()
    {
        // 自動更新は起動した exe の版で比べる。Core も同じ版でないと、新しい版を見落とす（以前は Core が 1.0.0 だった）
        var props = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Directory.Build.props");
        var expected = Updater.ParseVersion(System.Xml.Linq.XDocument.Load(props).Descendants("Version").Single().Value)!;
        var core = typeof(Updater).Assembly.GetName().Version!;
        Assert.Equal(expected, new Version(core.Major, core.Minor, core.Build));
    }

    [Fact]
    public void 今の版より新しくなければ知らせない()
    {
        Assert.Null(Updater.ParseRelease(Release("v0.4.1", true), new Version(0, 4, 1)));
        Assert.Null(Updater.ParseRelease(Release("v0.4.0", true), new Version(0, 4, 1)));
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.10.0", "1.10.0")]
    [InlineData("YuWords-0.4.1-win-x64.zip", "0.4.1")]
    public void 版を読む(string text, string expected) => Assert.Equal(Version.Parse(expected), Updater.ParseVersion(text));

    [Fact]
    public void 共有フォルダからいちばん新しい配布物を選ぶ()
    {
        foreach (var v in new[] { "0.4.0", "0.5.0", "0.10.0" })
            File.WriteAllText(Path.Combine(dir, $"YuWords-{v}-win-x64.zip"), v);
        File.WriteAllText(Path.Combine(dir, "YuWords-0.10.0-win-x64.zip.sha256"), $"{Hash.ToLowerInvariant()}  YuWords-0.10.0-win-x64.zip");
        File.WriteAllText(Path.Combine(dir, "ほかのファイル.zip"), "");
        var info = Updater.CheckFolder(dir, new Version(0, 4, 1))!;
        Assert.Equal(new Version(0, 10, 0), info.Version);
        Assert.Equal(Hash, info.Sha256);
        Assert.False(info.FromGitHub);
        Assert.Null(Updater.CheckFolder(dir, new Version(0, 10, 0)));
        Assert.Throws<DirectoryNotFoundException>(() => Updater.CheckFolder(Path.Combine(dir, "ない"), new Version(0, 1, 0)));
    }

    [Fact]
    public void 配布物を展開してYuWordsのフォルダを返す()
    {
        var zip = Path.Combine(dir, "YuWords-9.9.9-win-x64.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            z.CreateEntry("YuWords/YuWords.exe").Open().Dispose();
            z.CreateEntry("YuWords/dict/unidic/sys.dic").Open().Dispose();
        }
        var staged = Updater.Extract(zip, new Version(9, 9, 9));
        try
        {
            Assert.Equal("YuWords", Path.GetFileName(staged));
            Assert.True(File.Exists(Path.Combine(staged, "dict", "unidic", "sys.dic")));
        }
        finally { Directory.Delete(Path.GetDirectoryName(staged)!, true); }
    }

    [Fact]
    public void 開発中の実行は差し替えない()
    {
        Assert.True(Updater.IsDevelopmentBuild(@"C:\src\YuWords\src\YuWords\bin\Release\net8.0-windows\"));
        Assert.False(Updater.IsDevelopmentBuild(@"C:\Users\me\Documents\YuWords"));
    }

    [Fact]
    public void 差し替えのバッチを作る()
    {
        var saved = Updater.UpdateDir;
        Updater.UpdateDir = Path.Combine(dir, "update");
        try { CheckScript(); }
        finally { Updater.UpdateDir = saved; }
    }

    static void CheckScript()
    {
        var path = Updater.WriteApplyScript(@"C:\staging\YuWords", @"C:\Users\me\ソフト\YuWords", 1234, @"C:\log.txt");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = File.ReadAllText(path, Encoding.GetEncoding(932));
        Assert.Contains(@"""%SystemRoot%\System32\tasklist.exe"" /FI ""PID eq 1234""", text);
        Assert.Contains(@"robocopy ""C:\staging\YuWords"" ""C:\Users\me\ソフト\YuWords"" /E", text);
        Assert.Contains(@"start """" ""C:\Users\me\ソフト\YuWords\YuWords.exe"" --updated", text);
    }
}
