using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace YuWords.Core;

/// <summary>
/// IME のユーザー辞書を使う処理（読み取り・登録・削除・確認）を、別のプロセスで行う。
/// IME の中で落ちると、その種類の例外はアプリの中で受け止められずアプリごと終了するので、
/// 落ちても本体は「失敗した」と知らせるだけで済むようにする。
/// ExecutablePath が null のとき（コンソール版・テスト）は、同じプロセスで行う
/// </summary>
public static class ImeWorker
{
    /// <summary>別のプロセスとして起動する実行ファイル（画面版の YuWords.exe）。null なら同じプロセスで行う</summary>
    public static string? ExecutablePath { get; set; }

    public const string Argument = "--ime-worker";

    public sealed record Word(string Reading, string Text, string Pos);

    public sealed class Request
    {
        public string Op { get; set; } = "";
        public List<Word> Words { get; set; } = [];
    }

    public sealed class Response
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public List<Word> Words { get; set; } = [];
        public int Registered { get; set; }
        public int Skipped { get; set; }
        public List<Word> Failed { get; set; } = [];
        public List<Word> Succeeded { get; set; } = [];
        public int Deleted { get; set; }
        public int GetWordsResult { get; set; }
        /// <summary>登録のあとに辞書を読み直して確かめられたか（読み直せなければ false で、IME の返事をそのまま使う）</summary>
        public bool Verified { get; set; }
        /// <summary>IME は「登録した」と返したのに、読み直すと辞書に無かった語</summary>
        public List<Word> NotFound { get; set; } = [];
        /// <summary>試しに登録した結果（selftest）</summary>
        public string SelfTest { get; set; } = "";
    }

    static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>処理を行う。IME の辞書を使えなかった・別のプロセスが異常終了したときは InvalidOperationException</summary>
    public static Response Call(Request request, CancellationToken ct = default, int timeoutSeconds = 600)
    {
        var res = ExecutablePath == null ? Serve(request) : CallProcess(request, ct, timeoutSeconds);
        if (!res.Ok) throw new InvalidOperationException(res.Error ?? "IME のユーザー辞書を使えませんでした。");
        return res;
    }

    static Response CallProcess(Request request, CancellationToken ct, int timeoutSeconds)
    {
        var dir = Path.Combine(Path.GetTempPath(), "YuWords");
        Directory.CreateDirectory(dir);
        var id = Guid.NewGuid().ToString("N");
        var inPath = Path.Combine(dir, $"ime_{id}_in.json");
        var outPath = Path.Combine(dir, $"ime_{id}_out.json");
        try
        {
            File.WriteAllText(inPath, JsonSerializer.Serialize(request, Json));
            var psi = new ProcessStartInfo(ExecutablePath!) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(Argument);
            psi.ArgumentList.Add(inPath);
            psi.ArgumentList.Add(outPath);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("IME の処理を始められませんでした。");
            var sw = Stopwatch.StartNew();
            while (!p.WaitForExit(200))
            {
                if (ct.IsCancellationRequested || sw.Elapsed.TotalSeconds > timeoutSeconds)
                {
                    try { p.Kill(); } catch (Exception) { }
                    ct.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("IME のユーザー辞書の処理が終わりませんでした。");
                }
            }
            if (!File.Exists(outPath))
            {
                Log.Error($"IME の処理（{request.Op}）が異常終了しました（終了コード 0x{p.ExitCode:X8}）");
                return new Response
                {
                    Error = $"IME のユーザー辞書を使う処理が異常終了しました（0x{p.ExitCode:X8}）。" +
                            "［設定］→［抽出］の「IME のユーザー辞書を直接使う」を OFF にし、辞書ファイルに出力して取り込んでください。",
                };
            }
            return JsonSerializer.Deserialize<Response>(File.ReadAllText(outPath), Json) ?? new Response { Error = "結果を読めませんでした。" };
        }
        finally
        {
            try { File.Delete(inPath); } catch (Exception) { }
            try { File.Delete(outPath); } catch (Exception) { }
        }
    }

    /// <summary>別のプロセス側の入口（YuWords.exe --ime-worker 入力 出力）。STA スレッドで呼ぶこと</summary>
    public static int RunFromFiles(string inPath, string outPath)
    {
        Response res;
        try
        {
            var req = JsonSerializer.Deserialize<Request>(File.ReadAllText(inPath), Json) ?? new Request();
            res = Serve(req);
        }
        catch (Exception e)
        {
            res = new Response { Error = e.Message };
        }
        File.WriteAllText(outPath, JsonSerializer.Serialize(res, Json));
        return res.Ok ? 0 : 1;
    }

    /// <summary>
    /// 登録のあとに辞書を読み直し、IME が「登録した」と返した語が本当に入ったかを確かめる。
    /// 入っていなかった語は失敗に回す。辞書を読み直せないとき（0 語しか読めない など）は確かめずに IME の返事を使う
    /// </summary>
    static void Verify(ImeDictionary dic, Response res)
    {
        if (res.Succeeded.Count == 0) return;
        ApplyVerification(res, dic.GetUserWords().Select(w => (w.Word, w.Reading)).ToList());
    }

    /// <summary>読み直した辞書の語（present）と照らして、入っていなかった語を失敗に回す（テストのため分けてある）</summary>
    internal static void ApplyVerification(Response res, IReadOnlyCollection<(string Word, string Reading)> words)
    {
        if (words.Count == 0) return;   // 読み直せない（並びが合わず読めない など）。確かめられないので IME の返事を使う
        var present = words.ToHashSet();
        res.Verified = true;
        foreach (var w in res.Succeeded.ToList())
        {
            if (present.Contains((w.Text, w.Reading))) continue;
            res.Succeeded.Remove(w);
            res.NotFound.Add(w);
            res.Failed.Add(w);
        }
        int missing = res.NotFound.Count;
        // 数を合わせる（登録した → 見つからない、の分を減らす）
        int fromRegistered = Math.Min(missing, res.Registered);
        res.Registered -= fromRegistered;
        res.Skipped -= Math.Min(missing - fromRegistered, res.Skipped);
    }

    public const string TestReading = "ゆーわーずてすとご";
    public const string TestWord = "YuWords試験語";

    /// <summary>
    /// 試しに架空の語を登録し、読み直して入ったかを確かめてから消す。IME への直接登録がこの PC で本当に効くかの判定
    /// </summary>
    static string SelfTest(ImeDictionary dic)
    {
        var before = dic.GetUserWords();
        bool readable = before.Count > 0;
        var r = dic.Register(TestReading, TestWord, "名詞");
        if (r == RegisterResult.Failed) return "NG: 登録できませんでした（IME が受け付けませんでした）";
        var after = dic.GetUserWords();
        bool found = after.Any(w => w.Word == TestWord && w.Reading == TestReading);
        bool deleted = dic.Delete(TestReading, TestWord, "名詞");
        var gone = !dic.GetUserWords().Any(w => w.Word == TestWord && w.Reading == TestReading);
        if (found && gone) return "OK: 登録・読み直し・削除ができました（直接登録が使えます）";
        if (found) return "注意: 登録はできましたが、試験の語を消せませんでした（ユーザー辞書ツールで「" + TestWord + "」を消してください）";
        if (!readable && after.Count == 0) return "注意: 登録できたと返りましたが、辞書を読み直せず確かめられませんでした" + (deleted ? "" : "");
        return "NG: 登録できたと返りましたが、辞書に入っていませんでした（直接登録は効かないようです。辞書ファイルに出力して取り込んでください）";
    }

    /// <summary>実際の処理（同じプロセスで行う）</summary>
    public static Response Serve(Request req)
    {
        try
        {
            using var dic = ImeDictionary.Open();
            var res = new Response { Ok = true };
            switch (req.Op)
            {
                case "read":
                case "probe":
                    res.Words = dic.GetUserWords().Select(w => new Word(w.Reading, w.Word, w.Pos)).ToList();
                    res.GetWordsResult = dic.LastGetWordsResult;
                    break;
                case "register":
                    foreach (var w in req.Words)
                    {
                        if (w.Reading.Length == 0) { res.Failed.Add(w); continue; }
                        switch (dic.Register(w.Reading, w.Text, w.Pos))
                        {
                            case RegisterResult.Registered: res.Registered++; res.Succeeded.Add(w); break;
                            case RegisterResult.Exists: res.Skipped++; res.Succeeded.Add(w); break;
                            default: res.Failed.Add(w); break;
                        }
                    }
                    Verify(dic, res);
                    break;
                case "selftest":
                    res.SelfTest = SelfTest(dic);
                    break;
                case "delete":
                    foreach (var w in req.Words)
                        if (dic.Delete(w.Reading, w.Text, w.Pos)) res.Deleted++;
                    break;
                default:
                    return new Response { Error = $"不明な処理: {req.Op}" };
            }
            return res;
        }
        catch (InvalidOperationException e)
        {
            return new Response { Error = e.Message };
        }
    }
}
