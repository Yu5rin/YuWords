using YuWords.Core;

namespace YuWords.Tests;

/// <summary>配布物の隣に置く既定の設定（yuwords.defaults.json）</summary>
public sealed class DefaultsTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), $"yuwords_defaults_{Guid.NewGuid():N}");

    public DefaultsTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public void 書き出した既定の設定で初めての人が始まる()
    {
        var admin = new AppStore(Path.Combine(root, "admin"));
        admin.Settings.TeamDictionaryPath = @"\server\share\チームの辞書.csv";
        admin.Settings.UpdateSource = "folder";
        admin.Settings.UpdateFolder = @"\server\share\YuWords";
        admin.Settings.BackupPath = @"C:\Users\admin\OneDrive\dictionary_backup.txt";   // 個人の場所は入れない
        admin.Settings.Extractor.MinMails = 5;
        var defaults = Path.Combine(root, AppStore.DefaultsFileName);
        admin.WriteDefaults(defaults);
        Assert.DoesNotContain("OneDrive", File.ReadAllText(defaults));

        var user = new AppStore(Path.Combine(root, "user"), defaults);
        Assert.Equal((@"\server\share\チームの辞書.csv", "folder", @"\server\share\YuWords", 5, ""),
            (user.Settings.TeamDictionaryPath, user.Settings.UpdateSource, user.Settings.UpdateFolder, user.Settings.Extractor.MinMails, user.Settings.BackupPath));
        Assert.True(user.Settings.UpdateCheck);   // 書いていない項目はふだんの既定のまま
    }

    [Fact]
    public void すでに使っている人の設定は変えない()
    {
        var dir = Path.Combine(root, "user");
        var user = new AppStore(dir);
        user.Settings.TeamDictionaryPath = "自分で選んだ.csv";
        user.SaveSettings();
        var defaults = Path.Combine(root, AppStore.DefaultsFileName);
        File.WriteAllText(defaults, """{ "TeamDictionaryPath": "配布.csv" }""");
        Assert.Equal("自分で選んだ.csv", new AppStore(dir, defaults).Settings.TeamDictionaryPath);
    }

    [Fact]
    public void 壊れた既定の設定は使わない()
    {
        var defaults = Path.Combine(root, AppStore.DefaultsFileName);
        File.WriteAllText(defaults, "{ 壊れている");
        Assert.Equal("", new AppStore(Path.Combine(root, "user"), defaults).Settings.TeamDictionaryPath);
    }
}
