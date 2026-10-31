using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YuWords.Core;

/// <summary>新しい版の情報</summary>
/// <param name="Location">GitHub ならダウンロードの URL、共有フォルダ・ファイルならその場所</param>
/// <param name="Sha256">配布物の SHA256（分からなければ null）</param>
/// <param name="Notes">変更点（リリース本文の「## 変更点」の項目）</param>
public sealed record UpdateInfo(Version Version, string Location, long Size, string? Sha256, string Notes, bool FromGitHub);

/// <summary>
/// 自動更新。GitHub の最新リリース、または社内の共有フォルダに置いた配布物（YuWords-x.y.z-win-x64.zip）から新しい版を探し、
/// 取ってきて展開し、アプリを閉じたあとに差し替えて起動し直す。
/// 会社の環境で使えるよう、Windows のプロキシ設定とログイン中のアカウントの認証情報をそのまま使う。
/// 送るのは版の問い合わせだけで、メールや語などのデータは送らない
/// </summary>
public static class Updater
{
    public const string Repository = "Yu5rin/YuWords";
    static readonly Regex AssetName = new(@"^YuWords-(\d+\.\d+\.\d+)-win-x64\.zip$", RegexOptions.IgnoreCase);

    /// <summary>今の版（x.y.z）</summary>
    public static Version CurrentVersion
    {
        get
        {
            // 版は Directory.Build.props で全プロジェクトにそろえてある。起動した exe の版を使う
            var v = (System.Reflection.Assembly.GetEntryAssembly() ?? typeof(Updater).Assembly).GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>「v1.2.3」「1.2.3」を版にする。読めなければ null</summary>
    public static Version? ParseVersion(string? tag)
    {
        var m = Regex.Match(tag ?? "", @"(\d+)\.(\d+)\.(\d+)");
        return m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : null;
    }

    /// <summary>プロキシ（Windows の設定）と、ログイン中のアカウントの認証情報を使う HttpClient</summary>
    public static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = HttpClient.DefaultProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            UseDefaultCredentials = true,
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("YuWords", CurrentVersion.ToString()));
        return client;
    }

    // ===== 新しい版を探す =====

    /// <summary>GitHub の最新リリースを見る。今の版より新しくなければ null</summary>
    public static async Task<UpdateInfo?> CheckGitHubAsync(Version current, CancellationToken ct = default)
    {
        using var client = CreateClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var res = await client.SendAsync(req, cts.Token);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        if (res.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
            throw new HttpRequestException("プロキシの認証が必要です。社内の共有フォルダから更新するか、管理者に相談してください。");
        res.EnsureSuccessStatusCode();
        return ParseRelease(await res.Content.ReadAsStringAsync(cts.Token), current);
    }

    /// <summary>GitHub のリリースの JSON から、今の版より新しい配布物を取り出す（テストのため分けてある）</summary>
    public static UpdateInfo? ParseRelease(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        var version = ParseVersion(root.GetProperty("tag_name").GetString());
        if (version == null || version <= current) return null;
        var body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (!AssetName.IsMatch(name)) continue;
            // SHA256 は GitHub が付ける digest を使い、無ければリリース本文の「## ダウンロード」の表から読む
            string? sha = null;
            if (a.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:"))
                sha = digest["sha256:".Length..];
            sha ??= Sha256FromBody(body, name);
            return new UpdateInfo(version, a.GetProperty("browser_download_url").GetString()!, a.GetProperty("size").GetInt64(),
                sha?.ToUpperInvariant(), ChangesFromBody(body), true);
        }
        return null;
    }

    /// <summary>リリース本文の「| ファイル | サイズ | SHA256 |」の表から、そのファイルの SHA256 を読む</summary>
    public static string? Sha256FromBody(string body, string fileName)
    {
        foreach (var line in body.ReplaceLineEndings("\n").Split('\n'))
        {
            var cells = line.Split('|').Select(c => c.Trim().Trim('`')).ToArray();
            if (cells.Length >= 4 && cells.Contains(fileName))
                return cells.FirstOrDefault(c => Regex.IsMatch(c, "^[0-9A-Fa-f]{64}$"));
        }
        return null;
    }

    /// <summary>リリース本文の「## 変更点」の項目</summary>
    public static string ChangesFromBody(string body)
    {
        var lines = new List<string>();
        bool inside = false;
        foreach (var line in body.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("## "))
            {
                if (inside) break;
                inside = line.Trim() == "## 変更点";
                continue;
            }
            if (inside && line.Trim().Length > 0) lines.Add(line.Trim());
        }
        return string.Join("\n", lines);
    }

    /// <summary>社内の共有フォルダから、いちばん新しい配布物を探す。今の版より新しくなければ null</summary>
    public static UpdateInfo? CheckFolder(string folder, Version current)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"更新の確認先のフォルダが見つかりません: {folder}");
        var best = Directory.GetFiles(folder, "YuWords-*-win-x64.zip")
            .Select(f => (Path: f, Version: AssetName.Match(Path.GetFileName(f)) is { Success: true } m ? ParseVersion(m.Groups[1].Value) : null))
            .Where(x => x.Version != null && x.Version > current)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();
        return best.Path == null ? null : FromFile(best.Path, best.Version!);
    }

    /// <summary>手元の配布物（zip）から更新する。名前から版が読めなければ 0.0.0 として扱う</summary>
    public static UpdateInfo FromFile(string zipPath, Version? version = null)
    {
        version ??= AssetName.Match(Path.GetFileName(zipPath)) is { Success: true } m ? ParseVersion(m.Groups[1].Value)! : new Version(0, 0, 0);
        // 同じ場所に「zip の名前.sha256」があれば確かめる（社内の共有フォルダに置くときに一緒に置ける）
        string? sha = null;
        var shaFile = zipPath + ".sha256";
        if (File.Exists(shaFile))
            sha = Regex.Match(File.ReadAllText(shaFile), "[0-9A-Fa-f]{64}") is { Success: true } s ? s.Value.ToUpperInvariant() : null;
        return new UpdateInfo(version, zipPath, new FileInfo(zipPath).Length, sha, "", false);
    }

    // ===== 取ってきて展開する =====

    /// <summary>更新の作業フォルダ（取ってきた配布物・展開先・差し替えのバッチ）。テストでは別の場所にする</summary>
    public static string UpdateDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YuWords", "update");

    /// <summary>配布物を取ってきて SHA256 を確かめる。戻り値は zip の場所</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<(long Done, long Total)>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(UpdateDir);
        var zip = Path.Combine(UpdateDir, $"YuWords-{info.Version}-win-x64.zip");
        if (info.FromGitHub)
        {
            using var client = CreateClient();
            using var res = await client.GetAsync(info.Location, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();
            long total = res.Content.Headers.ContentLength ?? info.Size;
            await using var src = await res.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zip);
            var buffer = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                progress?.Report((done, total));
            }
        }
        else if (!string.Equals(Path.GetFullPath(info.Location), Path.GetFullPath(zip), StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => File.Copy(info.Location, zip, true), ct);
        }

        if (info.Sha256 != null)
        {
            var actual = Convert.ToHexString(await Task.Run(() => SHA256.HashData(File.ReadAllBytes(zip)), ct));
            if (!actual.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(zip);
                throw new InvalidDataException("取ってきた配布物が壊れています（SHA256 が一致しません）。もう一度試してください。");
            }
        }
        return zip;
    }

    /// <summary>
    /// zip を展開し、YuWords.exe のあるフォルダを返す。「インターネットから来たファイル」の印（Zone.Identifier）は外す
    /// （手で落とした zip を展開すると、会社の PC では起動を止められることがあるため）
    /// </summary>
    public static string Extract(string zipPath, Version version)
    {
        var staging = Path.Combine(UpdateDir, $"staging-{version}");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        ZipFile.ExtractToDirectory(zipPath, staging);
        var exe = Directory.GetFiles(staging, "YuWords.exe", SearchOption.AllDirectories).FirstOrDefault()
                  ?? throw new InvalidDataException("配布物の中に YuWords.exe がありません。");
        foreach (var f in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            RemoveZoneIdentifier(f);
        return Path.GetDirectoryName(exe)!;
    }

    public static void RemoveZoneIdentifier(string path)
    {
        try { File.Delete(path + ":Zone.Identifier"); } catch (Exception) { }
    }

    // ===== 差し替える =====

    /// <summary>開発中の実行（bin\Debug・bin\Release）なら、差し替えはしない</summary>
    public static bool IsDevelopmentBuild(string appDir) =>
        Regex.IsMatch(appDir.Replace('/', '\\'), @"\\bin\\(Debug|Release)\\", RegexOptions.IgnoreCase);

    /// <summary>アプリのフォルダに書き込めるか（Program Files などに置いていると、管理者でないと差し替えられない）</summary>
    public static bool CanWrite(string appDir)
    {
        try
        {
            var test = Path.Combine(appDir, $"yuwords_update_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(test, "");
            File.Delete(test);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// アプリが閉じるのを待って、展開した新しい版をアプリのフォルダに上書きし、起動し直すバッチを作る。
    /// 会社の PC でも動くよう、Windows に標準で入っている cmd と robocopy だけを使う
    /// </summary>
    public static string WriteApplyScript(string stagedDir, string appDir, int pid, string logPath)
    {
        var lines = new[]
        {
            "@echo off",
            "setlocal",
            ":wait",
            $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul",
            "if not errorlevel 1 (",
            "  ping -n 2 127.0.0.1 >nul",
            "  goto wait",
            ")",
            $"robocopy \"{stagedDir}\" \"{appDir}\" /E /R:10 /W:2 /NP /NFL /NDL >> \"{logPath}\"",
            "if %ERRORLEVEL% GEQ 8 (",
            $"  start \"\" \"{Path.Combine(appDir, "YuWords.exe")}\" --update-failed",
            "  exit /b 1",
            ")",
            $"start \"\" \"{Path.Combine(appDir, "YuWords.exe")}\" --updated",
        };
        var text = string.Join("\r\n", lines) + "\r\n";
        var path = Path.Combine(UpdateDir, "apply.cmd");
        Directory.CreateDirectory(UpdateDir);
        // cmd は既定でシステムのコードページ（日本語 Windows なら Shift_JIS）で読む。表せない文字があるパスなら UTF-8 にする
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var oem = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        try
        {
            File.WriteAllBytes(path, oem.GetBytes(text));
        }
        catch (EncoderFallbackException)
        {
            File.WriteAllText(path, "@chcp 65001 >nul\r\n" + text, new UTF8Encoding(false));
        }
        return path;
    }
}
