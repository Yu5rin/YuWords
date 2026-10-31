using System.Diagnostics;
using YuWords.Core;

namespace YuWords.Tests;

/// <summary>差し替えのバッチを実際に動かす（一時フォルダの中だけ）</summary>
[Collection("更新の作業フォルダ")]   // Updater.UpdateDir を共有するので順に動かす
public sealed class UpdateApplyTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), $"yuwords_apply_{Guid.NewGuid():N}", "ソフト ウェア");
    readonly string oldUpdateDir = Updater.UpdateDir;

    public UpdateApplyTests() => Updater.UpdateDir = Path.Combine(root, "update");

    public void Dispose()
    {
        Updater.UpdateDir = oldUpdateDir;
        // 起動し直した代わりの exe が終わるまで少し待って消す（消せなくても一時フォルダなので残してよい）
        for (int i = 0; i < 20; i++)
        {
            try
            {
                foreach (var f in Directory.GetFiles(Path.GetDirectoryName(root)!, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(Path.GetDirectoryName(root)!, true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Thread.Sleep(250); }
        }
    }

    [Fact]
    public void アプリが閉じたあとに上書きして起動し直す()
    {
        var app = Path.Combine(root, "YuWords");
        var staged = Path.Combine(root, "staging", "YuWords");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(Path.Combine(staged, "dict"));
        // 起動し直す YuWords.exe の代わりに、すぐ終わる whoami を置く
        var whoami = Path.Combine(Environment.SystemDirectory, "whoami.exe");
        File.Copy(whoami, Path.Combine(app, "YuWords.exe"));
        File.Copy(whoami, Path.Combine(staged, "YuWords.exe"));
        File.WriteAllText(Path.Combine(app, "keep.txt"), "残る");
        File.WriteAllText(Path.Combine(app, "old.txt"), "古い");
        File.WriteAllText(Path.Combine(staged, "old.txt"), "新しい");
        File.WriteAllText(Path.Combine(staged, "dict", "new.txt"), "足された");

        // すでに終わったプロセスの番号（待たずに差し替えに進む）
        using var done = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
        done.WaitForExit();
        var log = Path.Combine(root, "apply.log");
        var script = Updater.WriteApplyScript(staged, app, done.Id, log);

        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\"\"") { CreateNoWindow = true, UseShellExecute = false })!;
        Assert.True(p.WaitForExit(30000));
        Assert.Equal(0, p.ExitCode);
        Assert.Equal("新しい", File.ReadAllText(Path.Combine(app, "old.txt")));
        Assert.Equal("足された", File.ReadAllText(Path.Combine(app, "dict", "new.txt")));
        Assert.Equal("残る", File.ReadAllText(Path.Combine(app, "keep.txt")));
        Assert.True(File.Exists(log));
    }
}
