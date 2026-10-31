namespace YuWords.Core;

/// <summary>
/// 動作ログ（%APPDATA%\YuWords\logs\yuwords_yyyyMMdd.log）。不具合を調べるため、件数・時間・エラーだけを残す。
/// メール本文や抽出した語は書かない。14日より古いログは消す
/// </summary>
public static class Log
{
    static readonly object gate = new();
    static string? dir;

    public static string? Directory => dir;

    public static void Init(string appDir)
    {
        dir = Path.Combine(appDir, "logs");
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            foreach (var f in System.IO.Directory.GetFiles(dir, "yuwords_*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)) File.Delete(f);
        }
        catch (Exception) { }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? e = null) =>
        Write("ERROR", e == null ? message : $"{message}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");

    static void Write(string level, string message)
    {
        if (dir == null) return;
        try
        {
            lock (gate)
                File.AppendAllText(Path.Combine(dir, $"yuwords_{DateTime.Now:yyyyMMdd}.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}\r\n");
        }
        catch (Exception) { }
    }
}
