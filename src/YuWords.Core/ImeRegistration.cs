namespace YuWords.Core;

/// <summary>IME への直接登録の結果</summary>
public sealed class RegistrationReport
{
    public int Registered { get; set; }
    public int Skipped { get; set; }
    /// <summary>登録のあとに辞書を読み直して確かめられたか</summary>
    public bool Verified { get; set; }
    /// <summary>IME は「登録した」と返したのに、辞書に入っていなかった数</summary>
    public int NotFound { get; set; }
    /// <summary>直接登録が効いていない（1語も入らなかった）とみなせるか</summary>
    public bool LooksIneffective => Registered + Skipped == 0 && Failed.Count > 0;
    public List<(string Reading, string Word, string Pos)> Failed { get; } = [];
    /// <summary>登録できた語と既にあった語（登録済みリストに入れるもの）</summary>
    public List<(string Reading, string Word, string Pos)> Succeeded { get; } = [];
}

/// <summary>
/// 語の一覧を IME のユーザー辞書に登録し、登録済みリストと持ち出し用辞書を更新する。
/// IME のユーザー辞書を使う処理は <see cref="ImeWorker"/> を通す（画面版では別のプロセスで行う）
/// </summary>
public static class ImeRegistration
{
    /// <summary>IME のユーザー辞書を直接使ってよいか（設定。前回落ちたら false になる）</summary>
    public static bool Enabled { get; set; } = true;

    const string Disabled = "IME のユーザー辞書を直接使う設定が OFF です（［設定］→［抽出］）。";

    static List<ImeWorker.Word> ToWords(IEnumerable<(string Reading, string Word, string Pos)> words) =>
        words.Select(w => new ImeWorker.Word(w.Reading, w.Word, w.Pos)).ToList();

    /// <summary>
    /// 登録する。ユーザー辞書を開けない・処理が異常終了したときは InvalidOperationException（何も記録しない）
    /// </summary>
    public static RegistrationReport Run(IReadOnlyList<(string Reading, string Word, string Pos)> words, AppStore store,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (!Enabled) throw new InvalidOperationException(Disabled);
        var res = ImeWorker.Call(new ImeWorker.Request { Op = "register", Words = ToWords(words) }, ct);
        var report = new RegistrationReport { Registered = res.Registered, Skipped = res.Skipped, Verified = res.Verified, NotFound = res.NotFound.Count };
        if (res.NotFound.Count > 0) Log.Error($"IME が登録したと返したのに辞書に無かった語: {res.NotFound.Count} 語");
        report.Failed.AddRange(res.Failed.Select(w => (w.Reading, w.Text, w.Pos)));
        report.Succeeded.AddRange(res.Succeeded.Select(w => (w.Reading, w.Text, w.Pos)));
        progress?.Report(words.Count);
        store.AddRegistered(report.Succeeded.Select(w => (w.Word, w.Reading, w.Pos)));
        ImeLookup.ForgetConversions(report.Succeeded.Select(w => w.Reading));   // 登録で変換結果が変わるため
        return report;
    }

    /// <summary>試しに架空の語を登録し、読み直して確かめてから消す（環境チェック用）。結果の文</summary>
    public static string SelfTest() => ImeWorker.Call(new ImeWorker.Request { Op = "selftest" }, timeoutSeconds: 60).SelfTest;

    /// <summary>IME のユーザー辞書の語（よみ・表記）。開けなければ null</summary>
    public static HashSet<(string Word, string Reading)>? ReadUserDictionary()
    {
        if (!Enabled) return null;
        try
        {
            return ImeWorker.Call(new ImeWorker.Request { Op = "read" }).Words.Select(w => (w.Text, w.Reading)).ToHashSet();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Error("IME のユーザー辞書を読めませんでした", e);
            return null;
        }
    }

    /// <summary>IME のユーザー辞書の語をすべて登録済みリストに入れる（持ち出し用辞書に含めるため）。戻り値は追加した数</summary>
    public static int ImportUserDictionary(AppStore store)
    {
        if (!Enabled) throw new InvalidOperationException(Disabled);
        var res = ImeWorker.Call(new ImeWorker.Request { Op = "read" });
        return store.AddRegistered(res.Words.Select(w => (w.Text, w.Reading, w.Pos)));
    }

    /// <summary>
    /// 登録済みの語のよみ・品詞を直す。Microsoft IME に登録した語は、古い語を IME から消して新しい語を登録し直す
    /// （登録できなければ古い語を戻し、false を返して一覧は変えない）。Google に書き出した語は一覧だけ直す
    /// </summary>
    public static bool Edit(RegisteredWord old, string reading, string pos, AppStore store, CancellationToken ct = default)
    {
        reading = TermExtractor.KataToHira(reading.Trim());
        if (reading.Length == 0 || (reading == old.Reading && pos == old.Pos)) return false;
        if (old.Target == "MSIME")
        {
            if (!Enabled) throw new InvalidOperationException(Disabled);
            ImeWorker.Call(new ImeWorker.Request { Op = "delete", Words = ToWords([(old.Reading, old.Word, old.Pos)]) }, ct, 120);
            var res = ImeWorker.Call(new ImeWorker.Request { Op = "register", Words = ToWords([(reading, old.Word, pos)]) }, ct, 120);
            if (res.Succeeded.Count == 0)
            {
                ImeWorker.Call(new ImeWorker.Request { Op = "register", Words = ToWords([(old.Reading, old.Word, old.Pos)]) }, ct, 120);
                return false;
            }
            ImeLookup.ForgetConversions([old.Reading, reading]);
        }
        store.ReplaceRegistered(old, reading, pos);
        return true;
    }

    /// <summary>登録済みの語を IME のユーザー辞書から消し、登録済みリストからも外す。戻り値は IME から消せた数</summary>
    public static int Unregister(IReadOnlyList<RegisteredWord> words, AppStore store)
    {
        // Google 日本語入力に書き出した語は、Google の辞書ツールで消してもらう（ここでは一覧から外すだけ）
        var msime = words.Where(w => w.Target == "MSIME").ToList();
        int n = 0;
        if (msime.Count > 0)
        {
            if (!Enabled) throw new InvalidOperationException(Disabled);
            n = ImeWorker.Call(new ImeWorker.Request { Op = "delete", Words = ToWords(msime.Select(w => (w.Reading, w.Word, w.Pos))) }).Deleted;
        }
        store.RemoveRegistered(words);
        ImeLookup.ForgetConversions(words.Select(w => w.Reading));
        return n;
    }
}
