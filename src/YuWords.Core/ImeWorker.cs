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
