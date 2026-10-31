using System.Text.Encodings.Web;
using System.Text.Json;

namespace YuWords.Core;

/// <summary>アプリの設定（settings.json）</summary>
public sealed class AppSettings
{
    public ExtractorSettings Extractor { get; set; } = new();

    /// <summary>dictionary_backup.txt の保存先。空なら設定フォルダ</summary>
    public string BackupPath { get; set; } = "";

    /// <summary>登録済みの語を一覧に出すか（出す場合は薄く表示）</summary>
    public bool ShowRegistered { get; set; } = true;

    /// <summary>Outlook から読み込むとき、連絡先のふりがなで人名のよみを補うか</summary>
    public bool UseContactReadings { get; set; } = true;

    /// <summary>会社のアドレス帳（Exchange）のふりがなで人名のよみを補うか（大きなアドレス帳では時間がかかる）</summary>
    public bool UseGalReadings { get; set; }

    /// <summary>フォルダから読み込むとき、最近この日数以内に更新したファイルだけにする（0 ならすべて）</summary>
    public int FolderDays { get; set; }

    /// <summary>
    /// IME のユーザー辞書を直接使うか（直接登録・照合・取り込み）。使っている最中に落ちたら自動で false にする。
    /// false のときは、登録は辞書ファイルへの出力とユーザー辞書ツールで行う
    /// </summary>
    public bool UseImeDictionary { get; set; } = true;

    /// <summary>読み込んだとき、IME のユーザー辞書に既にある語を「登録済み」にするか</summary>
    public bool CheckImeDictionary { get; set; } = true;

    /// <summary>初回の案内（新しい PC への引っ越し）を出したか</summary>
    public bool Welcomed { get; set; }

    /// <summary>新しいメールの差分を自動で調べる頻度（タスク スケジューラに登録）</summary>
    public AutoCheckFrequency AutoCheck { get; set; }

    /// <summary>自動で調べる時刻（時）</summary>
    public int AutoCheckHour { get; set; } = 9;

    /// <summary>チームの辞書（社内の共有フォルダの CSV）。空なら使わない</summary>
    public string TeamDictionaryPath { get; set; } = "";

    /// <summary>起動したときに、チームの辞書の新しい語を知らせるか</summary>
    public bool TeamNotify { get; set; } = true;

    /// <summary>登録済みの語が出てくるかを最後に文章で調べた日時（使わなくなった語の判定に使う）</summary>
    public DateTime? LastUsageScan { get; set; }

    /// <summary>この日数のあいだ文章に出てこなければ「使わなくなったかもしれない」とする</summary>
    public int StaleDays { get; set; } = 180;

    /// <summary>起動したときに新しい版を確かめるか</summary>
    public bool UpdateCheck { get; set; } = true;

    /// <summary>新しい版の確認先: github / folder（社内の共有フォルダ）</summary>
    public string UpdateSource { get; set; } = "github";

    /// <summary>確認先が folder のときのフォルダ（YuWords-x.y.z-win-x64.zip を置く）</summary>
    public string UpdateFolder { get; set; } = "";

    /// <summary>最後に新しい版を確かめた日時</summary>
    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>「この版は知らせない」を選んだ版</summary>
    public string SkippedVersion { get; set; } = "";

    /// <summary>画面の色: auto（Windows に合わせる）/ light / dark</summary>
    public string ThemeMode { get; set; } = "auto";

    /// <summary>確認画面の見た目</summary>
    public UiState Ui { get; set; } = new();

    /// <summary>画面の文字の大きさ（ポイント）</summary>
    public float FontSize { get; set; } = 9f;

    /// <summary>前回 Outlook から読み込んだ日時（次回「新しいメールだけ」で使う）</summary>
    public DateTime? LastOutlookRead { get; set; }

    /// <summary>前回「フォルダから読み込む」で選んだフォルダ</summary>
    public string LastFolder { get; set; } = "";
}

/// <summary>前回読み込んだ送信メールでの、登録済みの語の使われ方（recent_use.json。語と通数だけで本文は残さない）</summary>
public sealed class RecentUse
{
    public Dictionary<string, int> Counts { get; set; } = [];
    public int Documents { get; set; }
    public DateTime? At { get; set; }
}

/// <summary>保留にした語1つ分（held.json）。選んだよみ・品詞を残しておく</summary>
public sealed record HeldWord(string Word, ReviewItem.State State, DateTime HeldAt);

/// <summary>確認画面の見た目（ウィンドウの位置・大きさ、列、例文欄の高さ）。次に開いたときに戻す</summary>
public sealed class UiState
{
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>幅・高さ（拡大率 100% のときのピクセル）。0 なら既定の大きさ</summary>
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
    /// <summary>例文欄の高さ（拡大率 100% のとき）</summary>
    public int ExampleHeight { get; set; }
    /// <summary>隠した列（列の名前）</summary>
    public List<string> HiddenColumns { get; set; } = [];
    /// <summary>利用者が変えた列の幅（列の名前 → 拡大率 100%・文字 9pt のときの幅）</summary>
    public Dictionary<string, int> ColumnWidths { get; set; } = [];
}

/// <summary>登録済みの語1つ分（registered.json）</summary>
/// <param name="Target">登録先。MSIME（Microsoft IME。既定）か Google（Google 日本語入力に書き出した語）</param>
/// <param name="Rescued">登録する前は IME で変換できなかった語か（効果の見える化に使う。v0.9.0 より前に登録した語は false）</param>
public sealed record RegisteredWord(string Word, string Reading, string Pos, DateTime RegisteredAt, string Target = "MSIME", bool Rescued = false);

/// <summary>
/// %APPDATA%\YuWords\ の永続データ。
/// settings.json / excluded.json / registered.json / dictionary_backup.txt
/// </summary>
public sealed class AppStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 日本語を \uXXXX にしない
    };

    public string Dir { get; }
    public AppSettings Settings { get; private set; } = new();
    public SortedSet<string> Excluded { get; private set; } = new(StringComparer.Ordinal);
    public List<RegisteredWord> Registered { get; private set; } = [];
    /// <summary>保留にした語（キーは抽出されたときの語句）</summary>
    public Dictionary<string, HeldWord> Held { get; private set; } = [];

    string SettingsPath => Path.Combine(Dir, "settings.json");
    string ExcludedPath => Path.Combine(Dir, "excluded.json");
    string RegisteredPath => Path.Combine(Dir, "registered.json");
    string HeldPath => Path.Combine(Dir, "held.json");
    string LearnedPath => Path.Combine(Dir, "learned.json");
    string TeamSeenPath => Path.Combine(Dir, "team_seen.json");
    string LastUsedPath => Path.Combine(Dir, "last_used.json");

    /// <summary>登録済みの語を最後に文章で見かけた日時（語句 → 日時）</summary>
    public Dictionary<string, DateTime> LastUsed { get; private set; } = [];

    /// <summary>文章を調べた結果を残す（見かけた語の日時と、調べた日時）</summary>
    public void MarkUsed(IEnumerable<string> words, DateTime when) =>
        MarkUsed(words.Distinct().ToDictionary(w => w, _ => 1), 0, when);

    /// <summary>文章を調べた結果を残す（語ごとの出たメールの数・調べた通数も）</summary>
    public void MarkUsed(IReadOnlyDictionary<string, int> counts, int documents, DateTime when)
    {
        foreach (var w in counts.Keys) LastUsed[w] = when;
        Save(LastUsedPath, LastUsed);
        if (documents > 0)
        {
            RecentUse = new RecentUse { Counts = counts.ToDictionary(kv => kv.Key, kv => kv.Value), Documents = documents, At = when };
            Save(RecentUsePath, RecentUse);
        }
        Settings.LastUsageScan = when;
        SaveSettings();
    }

    /// <summary>チームの辞書で、すでに知らせた語（語句 TAB よみ）</summary>
    public HashSet<string> TeamSeen { get; private set; } = [];

    public void MarkTeamSeen(IEnumerable<string> keys)
    {
        foreach (var k in keys) TeamSeen.Add(k);
        Save(TeamSeenPath, TeamSeen.ToList());
    }

    /// <summary>手で直したよみ（抽出された語句 → よみ）。次回の抽出で同じ語に使う</summary>
    public Dictionary<string, string> Learned { get; private set; } = [];

    public void Learn(string word, string reading)
    {
        if (reading.Length == 0) Learned.Remove(word);
        else Learned[word] = reading;
        Save(LearnedPath, Learned);
    }

    public void ClearLearned()
    {
        Learned.Clear();
        Save(LearnedPath, Learned);
    }

    public string BackupFile =>
        Settings.BackupPath.Length > 0 ? Settings.BackupPath : Path.Combine(Dir, "dictionary_backup.txt");

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YuWords");

    /// <summary>
    /// 配布物の隣に置く既定の設定（yuwords.defaults.json）の場所。社内で配るとき、チームの辞書や更新の確認先を全員にそろえるため。
    /// settings.json がまだ無い（初めて起動した）ときだけ読む。設定フォルダを指定しない（ふだんの場所を使う）ときに使う
    /// </summary>
    public static string? DefaultsPath { get; set; }

    public const string DefaultsFileName = "yuwords.defaults.json";

    /// <summary>既定の設定に書き出す項目（組織でそろえるもの。個人の場所や画面の状態は入れない）</summary>
    static readonly string[] DefaultsKeys =
    [
        nameof(AppSettings.TeamDictionaryPath), nameof(AppSettings.TeamNotify),
        nameof(AppSettings.UpdateCheck), nameof(AppSettings.UpdateSource), nameof(AppSettings.UpdateFolder),
        nameof(AppSettings.UseContactReadings), nameof(AppSettings.UseGalReadings),
        nameof(AppSettings.UseImeDictionary), nameof(AppSettings.CheckImeDictionary), nameof(AppSettings.Extractor),
    ];

    /// <summary>今の設定のうち組織でそろえる項目を、既定の設定ファイルとして書き出す</summary>
    public void WriteDefaults(string path)
    {
        var all = JsonSerializer.SerializeToNode(Settings, Json)!.AsObject();
        var obj = new System.Text.Json.Nodes.JsonObject();
        foreach (var k in DefaultsKeys)
            if (all[k] is { } v) obj[k] = v.DeepClone();
        File.WriteAllText(path, obj.ToJsonString(Json));
    }

    /// <summary>既定の設定を読む。無い・壊れているときは null（配布先のフォルダは書き込めないこともあるので退避はしない）</summary>
    static AppSettings? LoadDefaults(string? p)
    {
        if (p == null || !File.Exists(p)) return null;
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p), Json);
            if (s != null) Log.Info($"既定の設定を読みました（{p}）");
            return s;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("既定の設定を読めませんでした", e);
            return null;
        }
    }

    /// <param name="defaultsPath">既定の設定ファイル。省略すると、設定フォルダを指定しないときだけ <see cref="DefaultsPath"/> を使う</param>
    public AppStore(string? dir = null, string? defaultsPath = null)
    {
        Dir = dir ?? DefaultDir;
        Directory.CreateDirectory(Dir);
        Settings = Load<AppSettings>(SettingsPath) ?? (File.Exists(SettingsPath) ? null : LoadDefaults(defaultsPath ?? (dir == null ? DefaultsPath : null))) ?? new();
        Excluded = new(Load<List<string>>(ExcludedPath) ?? [], StringComparer.Ordinal);
        Registered = Load<List<RegisteredWord>>(RegisteredPath) ?? [];
        Held = (Load<List<HeldWord>>(HeldPath) ?? []).GroupBy(h => h.Word).ToDictionary(g => g.Key, g => g.Last());
        Learned = Load<Dictionary<string, string>>(LearnedPath) ?? [];
        TeamSeen = (Load<List<string>>(TeamSeenPath) ?? []).ToHashSet();
        LastUsed = Load<Dictionary<string, DateTime>>(LastUsedPath) ?? [];
        RecentUse = Load<RecentUse>(RecentUsePath) ?? new();
    }

    static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            // 壊れていたら退避して既定値で続ける
            File.Copy(path, path + ".broken", true);
            return null;
        }
    }

    static void Save<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, path, true);
    }

    public void SaveSettings() => Save(SettingsPath, Settings);

    public void SaveExcluded() => Save(ExcludedPath, Excluded.ToList());

    public void AddExcluded(IEnumerable<string> words)
    {
        foreach (var w in words) Excluded.Add(w);
        SaveExcluded();
    }

    /// <summary>語を保留にする（よみ・品詞などの選択も残す）</summary>
    public void Hold(IEnumerable<ReviewItem> items)
    {
        foreach (var i in items) Held[i.Source.Word] = new HeldWord(i.Source.Word, i.Save() with { Held = true }, DateTime.Now);
        Save(HeldPath, Held.Values.ToList());
    }

    public void Unhold(IEnumerable<string> words)
    {
        foreach (var w in words) Held.Remove(w);
        Save(HeldPath, Held.Values.ToList());
    }

    HashSet<(string, string)>? registeredSet;

    /// <summary>Microsoft IME に登録（出力）済みか。一覧の「登録済み」はこれで判定する</summary>
    public bool IsRegistered(string word, string reading) =>
        (registeredSet ??= Registered.Where(r => r.Target == "MSIME").Select(r => (r.Word, r.Reading)).ToHashSet()).Contains((word, reading));

    /// <summary>指定した登録先に登録（書き出し）済みか</summary>
    public bool IsRegisteredIn(string target, string word, string reading) =>
        Registered.Any(r => r.Target == target && r.Word == word && r.Reading == reading);

    /// <summary>一覧の保留の状態を held.json に合わせる（保留中の語は今の選択で、保留を外した語は消す）。一覧に無い語はそのまま</summary>
    public void SyncHeld(IEnumerable<ReviewItem> items)
    {
        bool changed = false;
        foreach (var i in items)
        {
            if (i.Held)
            {
                var st = i.Save();
                if (!Held.TryGetValue(i.Source.Word, out var h) || h.State != st)
                {
                    Held[i.Source.Word] = new HeldWord(i.Source.Word, st, h?.HeldAt ?? DateTime.Now);
                    changed = true;
                }
            }
            else if (Held.Remove(i.Source.Word)) changed = true;
        }
        if (changed) Save(HeldPath, Held.Values.ToList());
    }

    /// <summary>最後に持ち出し用辞書を書けなかったときの理由（書けたら null）</summary>
    public string? BackupError { get; private set; }

    /// <summary>
    /// 登録済みリストに追加し、dictionary_backup.txt を作り直す。
    /// 同じ語・よみが既にあれば追加しない。戻り値は新しく追加した数
    /// </summary>
    public int AddRegistered(IEnumerable<(string Word, string Reading, string Pos)> words, string target = "MSIME")
    {
        int added = 0;
        var now = DateTime.Now;
        foreach (var (w, r, p) in words)
        {
            if (r.Length == 0 || IsRegisteredIn(target, w, r)) continue;
            Registered.Add(new RegisteredWord(w, r, p, now, target));
            added++;
        }
        registeredSet = null;
        Save(RegisteredPath, Registered);
        TryWriteBackup();
        return added;
    }

    /// <summary>登録済みリストから外し、dictionary_backup.txt を作り直す</summary>
    public void RemoveRegistered(IEnumerable<RegisteredWord> words)
    {
        var set = words.ToHashSet();
        Registered.RemoveAll(set.Contains);
        registeredSet = null;
        Save(RegisteredPath, Registered);
        TryWriteBackup();
    }

    /// <summary>登録済みの語のよみ・品詞を置き換え（登録日はそのまま）、持ち出し用辞書を作り直す</summary>
    public void ReplaceRegistered(RegisteredWord old, string reading, string pos)
    {
        int i = Registered.IndexOf(old);
        if (i < 0) return;
        // 同じ語・よみ・登録先がほかにあれば、重ならないよう古い方を外すだけにする
        if (reading != old.Reading && Registered.Any(r => r != old && r.Target == old.Target && r.Word == old.Word && r.Reading == reading))
            Registered.RemoveAt(i);
        else
            Registered[i] = old with { Reading = reading, Pos = pos };
        registeredSet = null;
        Save(RegisteredPath, Registered);
        TryWriteBackup();
    }

    /// <summary>登録する前は IME で変換できなかった語に印を付ける（語句が同じ行すべて）</summary>
    public void MarkRescued(IEnumerable<string> words)
    {
        var set = words.ToHashSet();
        if (set.Count == 0) return;
        bool changed = false;
        for (int i = 0; i < Registered.Count; i++)
            if (!Registered[i].Rescued && set.Contains(Registered[i].Word))
                (Registered[i], changed) = (Registered[i] with { Rescued = true }, true);
        if (changed) Save(RegisteredPath, Registered);
    }

    string RecentUsePath => Path.Combine(Dir, "recent_use.json");

    /// <summary>前回読み込んだ送信メールで、登録済みの語が出たメールの数（語句 → 通数）と、読み込んだ通数</summary>
    public RecentUse RecentUse { get; private set; } = new();

    /// <summary>持ち出し用辞書を今の登録済みの語で書き直す（引っ越しのあとなど）</summary>
    public void RefreshBackup() => TryWriteBackup();

    /// <summary>
    /// 持ち出し用辞書を書く。書けなくても（保存先が無い・書き込めない）登録自体は済んでいるので、例外にせず BackupError に残す
    /// </summary>
    void TryWriteBackup()
    {
        try
        {
            WriteBackup();
            BackupError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            BackupError = e.Message;
            Log.Error("持ち出し用辞書を書けませんでした", e);
        }
    }

    /// <summary>その場所に持ち出し用辞書を書けるか（フォルダを指していないか・書き込めるか）。書けなければ理由</summary>
    public static string? CheckBackupPath(string path)
    {
        try
        {
            if (Directory.Exists(path)) return "フォルダではなく、ファイルの名前まで指定してください（例: …\\dictionary_backup.txt）";
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(dir)) return "保存先のフォルダが分かりません";
            Directory.CreateDirectory(dir);
            var test = Path.Combine(dir, $"yuwords_write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(test, "");
            File.Delete(test);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return e.Message;
        }
    }

    /// <summary>持ち出し用辞書の古い版を残しておく数</summary>
    public const int BackupGenerations = 10;

    public string BackupHistoryDir => Path.Combine(Dir, "backups");

    /// <summary>
    /// これまで登録した全語を IME 取り込み形式で書く（新PCへの持ち出し用）。
    /// 中身が変わるときは、前の版を設定フォルダの backups に日時付きで残す（新しい順に 10 個）
    /// </summary>
    public void WriteBackup()
    {
        var dir = Path.GetDirectoryName(BackupFile);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = Path.Combine(Dir, "dictionary_backup.new");
        OutputWriters.WriteImeText(tmp, Registered.DistinctBy(r => (r.Word, r.Reading)).Select(r => (r.Reading, r.Word, r.Pos)));
        if (File.Exists(BackupFile))
        {
            if (File.ReadAllBytes(BackupFile).AsSpan().SequenceEqual(File.ReadAllBytes(tmp)))
            {
                File.Delete(tmp);
                return;
            }
            Directory.CreateDirectory(BackupHistoryDir);
            var name = $"dictionary_backup_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt";
            File.Copy(BackupFile, Path.Combine(BackupHistoryDir, name), true);
            foreach (var old in Directory.GetFiles(BackupHistoryDir, "dictionary_backup_*.txt")
                         .OrderDescending(StringComparer.Ordinal).Skip(BackupGenerations))
                File.Delete(old);
        }
        File.Move(tmp, BackupFile, true);
    }
}
