using System.Diagnostics;

namespace YuWords.Core;

/// <summary>
/// Google 日本語入力への登録の手伝い。Google 日本語入力にはほかのアプリから単語を登録する公開の仕組みが無いので、
/// Google 形式の辞書ファイルを書き出し、Google の辞書ツールを開いて、インポートは利用者に行ってもらう
/// </summary>
public static class GoogleIme
{
    /// <summary>登録済みの記録で、Google 日本語入力に書き出した語の登録先</summary>
    public const string Target = "Google";

    /// <summary>Google 日本語入力の設定ツール（GoogleIMEJaTool.exe）の場所。入っていなければ null</summary>
    public static string? ToolPath()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var path = Path.Combine(root, "Google", "Google Japanese Input", "GoogleIMEJaTool.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static bool IsInstalled => ToolPath() != null;

    /// <summary>Google 日本語入力の辞書ツールを開く。開けなければ理由を返す（開けたら null）</summary>
    public static string? OpenDictionaryTool()
    {
        var tool = ToolPath();
        if (tool == null) return "Google 日本語入力が見つかりませんでした。";
        try
        {
            Process.Start(new ProcessStartInfo(tool, "--mode=dictionary_tool") { UseShellExecute = false });
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// 語を Google 形式の辞書ファイルに書き出す（設定フォルダの google_import に日時付きで）。戻り値は書いたファイルと語数
    /// </summary>
    public static (string Path, int Count) Export(string appDir, IEnumerable<(string Reading, string Word, string Pos)> words)
    {
        var dir = Path.Combine(appDir, "google_import");
        Directory.CreateDirectory(dir);
        // 古い書き出しは 10 個まで残す
        foreach (var old in Directory.GetFiles(dir, "yuwords_google_*.txt").OrderDescending(StringComparer.Ordinal).Skip(9))
            File.Delete(old);
        var path = Path.Combine(dir, $"yuwords_google_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        int n = DictionaryFormats.Write(path, words, DictFormat.Google);
        return (path, n);
    }
}
