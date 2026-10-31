using System.IO.Compression;
using YuWords.Core;

namespace YuWords.Tests;

/// <summary>新しい PC への引っ越し（まとめて書き出す・読み込む）。例はすべて架空</summary>
public sealed class MigrationTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), $"yuwords_move_{Guid.NewGuid():N}");
    string OldPc => Path.Combine(root, "old");
    string NewPc => Path.Combine(root, "new");
    string Zip => Path.Combine(root, "YuWords_引っ越し.zip");

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    AppStore OldStore()
    {
        var s = new AppStore(OldPc);
        s.AddRegistered([("工符", "こうふ", "名詞"), ("高田", "たかだ", "人名")]);
        s.Excluded.Add("お疲れ様");
        s.SaveExcluded();
        s.Learn("桐電", "きりでん");
        s.Settings.Extractor.ReadingRules = ["係=がかり", "# メモ"];
        s.Settings.AutoCheck = AutoCheckFrequency.Weekly;
        s.Settings.BackupPath = Path.Combine(root, "この PC に無いフォルダ", "dictionary_backup.txt");
        s.Settings.TeamDictionaryPath = @"\\server\share\チームの辞書.csv";
        s.SaveSettings();
        return s;
    }

    [Fact]
    public void 書き出して新しいPCで読み込む()
    {
        Directory.CreateDirectory(root);
        var old = OldStore();
        Migration.Export(old, Zip);
        Assert.True(Migration.IsMigrationFile(Zip));

        var sum = Migration.Inspect(Zip);
        Assert.Equal((2, 1, 1, 1), (sum.Registered, sum.Excluded, sum.Learned, sum.Rules));

        // 新しい PC にも少しだけ設定がある
        var fresh = new AppStore(NewPc);
        fresh.Excluded.Add("いま");
        fresh.SaveExcluded();
        var backup = Migration.Import(Zip, NewPc);
        Assert.True(File.Exists(Path.Combine(backup, "excluded.json")));   // 置き換える前のものを控える

        var moved = new AppStore(NewPc);
        Assert.Equal(["工符", "高田"], moved.Registered.Select(r => r.Word));
        Assert.Equal(["お疲れ様"], moved.Excluded);
        Assert.Equal("きりでん", moved.Learned["桐電"]);
        Assert.Equal(["係=がかり", "# メモ"], moved.Settings.Extractor.ReadingRules);
        Assert.Equal(@"\\server\share\チームの辞書.csv", moved.Settings.TeamDictionaryPath);
        // 前の PC だけに意味のある設定は直す
        Assert.Equal(AutoCheckFrequency.Off, moved.Settings.AutoCheck);
        Assert.Equal("", moved.Settings.BackupPath);
        Assert.True(moved.Settings.Welcomed);
    }

    [Fact]
    public void 途中の一覧は入れない()
    {
        Directory.CreateDirectory(root);
        var old = OldStore();
        File.WriteAllText(ReviewSession.PathIn(OldPc), "{}");
        Migration.Export(old, Zip);
        using var zip = ZipFile.OpenRead(Zip);
        Assert.DoesNotContain(zip.Entries, e => e.Name == "session.json");
        Assert.Contains(zip.Entries, e => e.Name == "registered.json");
    }

    [Fact]
    public void 引っ越しファイルでないzipは読み込まない()
    {
        Directory.CreateDirectory(root);
        using (var zip = ZipFile.Open(Zip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(zip.CreateEntry("settings.json").Open())) w.Write("{}");
        Assert.False(Migration.IsMigrationFile(Zip));
        Assert.Throws<InvalidDataException>(() => Migration.Import(Zip, NewPc));
        Assert.False(Migration.IsMigrationFile(Path.Combine(root, "無い.zip")));
    }
}
