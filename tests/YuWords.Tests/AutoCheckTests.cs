using YuWords.Core;

namespace YuWords.Tests;

public sealed class AutoCheckTests
{
    [Fact]
    public void 毎月の登録の引数()
    {
        var args = AutoCheck.CreateArguments(@"C:\Users\me\ソフト\YuWords\YuWords.exe", AutoCheckFrequency.Monthly, 9);
        Assert.Equal("/Create /TN \"YuWords 新しい候補のチェック\" /TR \"\\\"C:\\Users\\me\\ソフト\\YuWords\\YuWords.exe\\\" --check-new\" /SC MONTHLY /D 1 /ST 09:00 /F", args);
    }

    [Fact]
    public void 毎週の登録の引数()
    {
        var args = AutoCheck.CreateArguments(@"C:\a\YuWords.exe", AutoCheckFrequency.Weekly, 25);
        Assert.Contains("/SC WEEKLY /D MON /ST 23:00", args);
    }

    [Fact]
    public void 削除の引数()
    {
        Assert.Equal("/Delete /TN \"YuWords 新しい候補のチェック\" /F", AutoCheck.DeleteArguments());
        Assert.Throws<ArgumentException>(() => AutoCheck.CreateArguments("x", AutoCheckFrequency.Off, 9));
    }

    [Fact]
    public void 前回の読み込みが無ければ調べない()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"yuwords_auto_{Guid.NewGuid():N}");
        try { Assert.Null(AutoCheck.FindNewWords(new AppStore(dir))); }
        finally { Directory.Delete(dir, true); }
    }
}
