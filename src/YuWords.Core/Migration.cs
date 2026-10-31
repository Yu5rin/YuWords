using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace YuWords.Core;

/// <summary>
/// 新しい PC への引っ越し。設定・除外リスト・登録済みの語・直したよみなどを1つの zip にまとめて書き出し、読み込む。
/// 途中の一覧（session.json。メールの例文を含む）とログは入れない
/// </summary>
public static class Migration
{
    /// <summary>引っ越しに入れるファイル（%APPDATA%\YuWords の中の名前）</summary>
    public static readonly string[] Files =
        ["settings.json", "excluded.json", "registered.json", "held.json", "learned.json", "last_used.json", "team_seen.json", "notes.json"];

    /// <summary>YuWords の引っ越しファイルであることの印（版と作った日時を書く）</summary>
    const string Marker = "yuwords_migration.txt";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>引っ越しファイルの中身のあらまし</summary>
    public sealed record Summary(string FromVersion, string CreatedAt, int Registered, int Excluded, int Learned, int Rules, int Held);

    /// <summary>書き出す。戻り値は入れたファイルの数</summary>
    public static int Export(AppStore store, string zipPath)
    {
        store.SaveSettings();
        var tmp = zipPath + ".tmp";
        int n = 0;
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var name in Files)
            {
                var path = Path.Combine(store.Dir, name);
                if (!File.Exists(path)) continue;
                zip.CreateEntryFromFile(path, name);
                n++;
            }
            using var w = new StreamWriter(zip.CreateEntry(Marker).Open(), new UTF8Encoding(false));
            w.WriteLine($"YuWords {Updater.CurrentVersion}");
            w.WriteLine(DateTime.Now.ToString("yyyy/MM/dd HH:mm"));
        }
        File.Move(tmp, zipPath, true);
        return n;
    }

    /// <summary>YuWords の引っ越しファイルか（zip で、印が入っている）</summary>
    public static bool IsMigrationFile(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.GetEntry(Marker) != null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>中身を確かめる。引っ越しファイルでなければ InvalidDataException</summary>
    public static Summary Inspect(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var marker = zip.GetEntry(Marker) ?? throw new InvalidDataException("YuWords の引っ越しファイルではありません。");
        string[] head;
        using (var r = new StreamReader(marker.Open(), Encoding.UTF8)) head = r.ReadToEnd().ReplaceLineEndings("\n").Split('\n');
        T? Read<T>(string name) where T : class
        {
            var e = zip.GetEntry(name);
            if (e == null) return null;
            using var s = e.Open();
            try { return JsonSerializer.Deserialize<T>(s, Json); }
            catch (JsonException) { return null; }
        }
        var settings = Read<AppSettings>("settings.json");
        return new Summary(
            head.Length > 0 ? head[0].Replace("YuWords ", "").Trim() : "",
            head.Length > 1 ? head[1].Trim() : "",
            Read<List<RegisteredWord>>("registered.json")?.Count ?? 0,
            Read<List<string>>("excluded.json")?.Count ?? 0,
            Read<Dictionary<string, string>>("learned.json")?.Count ?? 0,
            settings?.Extractor.ReadingRules.Count(l => l.Contains('=')) ?? 0,
            Read<List<HeldWord>>("held.json")?.Count ?? 0);
    }

    /// <summary>
    /// 読み込む（dir の今のファイルを置き換える）。置き換える前のファイルは dir\before_import_日時 に控える。戻り値は控えたフォルダ。
    /// 前の PC だけに意味のある設定（持ち出し用辞書の保存先が無い・自動チェックの予約・更新の確認日時）は直す。
    /// 読み込んだあとは AppStore を作り直すこと
    /// </summary>
    public static string Import(string zipPath, string dir)
    {
        Inspect(zipPath);   // 引っ越しファイルでなければここで止める
        Directory.CreateDirectory(dir);
        var backup = Path.Combine(dir, $"before_import_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(backup);
        foreach (var name in Files)
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) File.Copy(path, Path.Combine(backup, name), true);
        }
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var name in Files)
            {
                if (zip.GetEntry(name) is not { } e) continue;   // 名前の決まったものだけを取り出す（zip の中のパスは使わない）
                var path = Path.Combine(dir, name);
                e.ExtractToFile(path + ".tmp", true);
                File.Move(path + ".tmp", path, true);
            }
        }
        FixSettingsForThisPc(Path.Combine(dir, "settings.json"));
        return backup;
    }

    static void FixSettingsForThisPc(string path)
    {
        if (!File.Exists(path)) return;
        AppSettings? s;
        try { s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json); }
        catch (JsonException) { return; }
        if (s == null) return;
        // 持ち出し用辞書の保存先のフォルダがこの PC に無ければ、設定フォルダに戻す（OneDrive の場所が同じなら、そのまま使う）
        if (s.BackupPath.Length > 0 && !Directory.Exists(Path.GetDirectoryName(s.BackupPath) ?? "")) s.BackupPath = "";
        // タスク スケジューラの予約は PC ごとなので引き継がない（設定画面で選び直す）
        s.AutoCheck = AutoCheckFrequency.Off;
        s.LastUpdateCheck = null;
        s.Welcomed = true;
        File.WriteAllText(path, JsonSerializer.Serialize(s, Json));
    }
}
