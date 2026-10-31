using System.Collections.Concurrent;

namespace YuWords.Core;

/// <summary>
/// IME への問い合わせ（表記 → よみ、よみ → 変換結果）をまとめて行う。
/// 1回の問い合わせは数十ミリ秒かかるので、複数の STA スレッドにそれぞれ IME の接続を作って並べて問い合わせ、
/// 結果はアプリを閉じるまで覚えて、数え直し・追加読み込みで使い回す
/// </summary>
public static class ImeLookup
{
    static readonly ConcurrentDictionary<string, string> phonetic = new();
    static readonly ConcurrentDictionary<string, string> conversion = new();

    /// <summary>覚えている変換結果を捨てる（登録で変換結果が変わったとき）</summary>
    public static void ForgetConversions(IEnumerable<string> readings)
    {
        foreach (var r in readings) conversion.TryRemove(r, out _);
    }

    /// <summary>
    /// 候補に IME のよみと変換結果を付ける。戻り値は IME に接続できなかったときの理由（できたら null）
    /// </summary>
    public static string? Fill(IReadOnlyList<Candidate> rows, CancellationToken ct = default, int threads = 4)
    {
        // 1. 表記 → よみ（よみが無い語は、ここで決まったよみで変換結果を取る）
        var words = rows.Select(c => c.Word).Where(w => !phonetic.ContainsKey(w)).Distinct().ToList();
        var error = Run(words, threads, ct, (ime, w) => phonetic[w] = ime.Phonetic(w));
        foreach (var c in rows) Extraction.ApplyImeReading(c, phonetic.GetValueOrDefault(c.Word, ""));

        // 2. よみ → 変換結果
        var readings = rows.Select(c => c.Reading).Where(r => r.Length > 0 && !conversion.ContainsKey(r)).Distinct().ToList();
        error ??= Run(readings, threads, ct, (ime, r) => conversion[r] = ime.Convert(r));
        foreach (var c in rows) c.ImeConversion = conversion.GetValueOrDefault(c.Reading, "");
        return error;
    }

    /// <summary>items を threads 本の STA スレッドに分けて、それぞれの IME 接続で処理する</summary>
    static string? Run(List<string> items, int threads, CancellationToken ct, Action<ImeConverter, string> work)
    {
        if (items.Count == 0) return null;
        threads = Math.Clamp(Math.Min(threads, items.Count / 20 + 1), 1, 8);
        var queue = new ConcurrentQueue<string>(items);
        string? error = null;
        var list = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
        {
            using var ime = new ImeConverter();
            if (!ime.IsAvailable) { error = ime.Error; return; }
            while (!ct.IsCancellationRequested && queue.TryDequeue(out var item)) work(ime, item);
        }) { IsBackground = true }).ToList();
        foreach (var t in list)
        {
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
        foreach (var t in list) t.Join();
        ct.ThrowIfCancellationRequested();
        return error;
    }
}
