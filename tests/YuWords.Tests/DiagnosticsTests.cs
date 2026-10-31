using YuWords.Core;

namespace YuWords.Tests;

public sealed class DiagnosticsTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), $"yuwords_diag_{Guid.NewGuid():N}");

    public DiagnosticsTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void 結果を貼り付けやすい文字にする()
    {
        var text = Diagnostics.Format([
            new CheckResult("形態素解析の辞書", CheckStatus.OK, "あり"),
            new CheckResult("Outlook クラシック版", CheckStatus.NG, "見つかりません"),
        ], "0.3.0");
        Assert.StartsWith("YuWords 0.3.0 環境チェック", text);
        Assert.Contains("[OK] 形態素解析の辞書: あり\r\n", text);
        Assert.Contains("[NG] Outlook クラシック版: 見つかりません\r\n", text);
    }

    [Fact]
    public void ログは日付ごとのファイルに書き古いものは消す()
    {
        var logs = Path.Combine(dir, "logs");
        Directory.CreateDirectory(logs);
        var old = Path.Combine(logs, "yuwords_20000101.log");
        File.WriteAllText(old, "古い");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));

        Log.Init(dir);
        Log.Info("読み込み: 10 件");
        Log.Error("失敗", new InvalidOperationException("テスト"));

        Assert.False(File.Exists(old));
        var text = File.ReadAllText(Path.Combine(logs, $"yuwords_{DateTime.Now:yyyyMMdd}.log"));
        Assert.Contains("[INFO] 読み込み: 10 件", text);
        Assert.Contains("[ERROR] 失敗: InvalidOperationException: テスト", text);
    }
}
