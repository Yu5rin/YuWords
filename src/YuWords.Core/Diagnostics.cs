using System.Diagnostics;

namespace YuWords.Core;

/// <summary>環境チェックの1項目</summary>
public sealed record CheckResult(string Name, CheckStatus Status, string Detail);

public enum CheckStatus { OK, Warning, NG }

/// <summary>
/// 動作に必要なもの（辞書・Outlook・IME）が使えるかをまとめて確かめる。
/// IME のユーザー辞書は開いて読むだけで、何も登録しない。Outlook は起動しない
/// </summary>
public static class Diagnostics
{
    public static List<CheckResult> Run(AppStore store)
    {
        var list = new List<CheckResult>();
        void Add(string name, Func<(CheckStatus, string)> check)
        {
            try
            {
                var (s, d) = check();
                list.Add(new CheckResult(name, s, d));
            }
            catch (Exception e)
            {
                list.Add(new CheckResult(name, CheckStatus.NG, e.Message));
            }
        }

        Add("形態素解析の辞書", () =>
        {
            var dir = MorphTokenizer.FindDictionary();
            using var tok = new MorphTokenizer(dir);
            var n = tok.Tokenize("品質保証課へ連絡").Count;
            var mb = new FileInfo(Path.Combine(dir, "sys.dic")).Length / 1024 / 1024;
            return (CheckStatus.OK, $"{dir}（sys.dic {mb}MB、試しに解析 {n} 語）");
        });

        Add("Outlook クラシック版", () =>
        {
            if (Type.GetTypeFromProgID("Outlook.Application") == null)
            {
                bool newOutlook = Process.GetProcessesByName("olk").Length > 0;
                return (CheckStatus.NG, newOutlook
                    ? "新しい Outlook だけが入っています。新しい Outlook は読み込みに対応していません"
                    : "見つかりません（メール以外の取り込み元は使えます）");
            }
            bool running = Process.GetProcessesByName("OUTLOOK").Length > 0;
            return (running ? CheckStatus.OK : CheckStatus.Warning,
                running ? "入っています（起動中）" : "入っています。読み込む前に起動しておくと確実です");
        });

        Add("IME の変換（IFELanguage）", () =>
        {
            using var ime = new ImeConverter();
            if (!ime.IsAvailable) return (CheckStatus.NG, ime.Error ?? "接続できません");
            var conv = ime.Convert("ひんしつほしょうか");
            var reading = ime.Phonetic("品質保証課");
            return (conv.Length > 0 && reading.Length > 0 ? CheckStatus.OK : CheckStatus.Warning,
                $"ひんしつほしょうか → {conv}、品質保証課 → {reading}");
        });

        Add("IME のユーザー辞書（IFEDictionary）", () =>
        {
            // 別のプロセスで開いて読むだけ（IME の中で落ちても、ここは「NG」になるだけ）
            var res = ImeWorker.Call(new ImeWorker.Request { Op = "probe" }, timeoutSeconds: 60);
            var words = res.Words;
            var pos = string.Join("、", ImeDictionary.PosNumbers.Select(kv => $"{kv.Key}={kv.Value}"));
            int hr = res.GetWordsResult;
            // IFED_S_EMPTY_DICTIONARY（0x00047201）・IFED_E_NO_ENTRY（0x80047304）は「登録されている語なし」。ほかの負の値は読み取りの失敗
            bool empty = hr is 0x00047201 or unchecked((int)0x80047304);
            var result = empty ? "登録されている語なし" : hr < 0 ? $"語を読めませんでした（0x{hr:X8}）" : "読めました";
            return (hr < 0 && !empty ? CheckStatus.Warning : CheckStatus.OK,
                $"開けました。登録されている語 {words.Count} 語（{result}）。品詞番号 {pos}");
        });

        Add("ユーザー辞書ツール", () => ImeDictionary.DictionaryToolPath() is { } p
            ? (CheckStatus.OK, p)
            : (CheckStatus.Warning, "見つかりません（IME のメニューから開いてください）"));

        Add("Google 日本語入力", () => GoogleIme.ToolPath() is { } g
            ? (CheckStatus.OK, $"辞書ツールあり（{g}）。「Google 日本語入力に登録」で書き出して取り込めます")
            : (CheckStatus.OK, "入っていません（Microsoft IME だけを使います）"));

        Add("設定フォルダ", () =>
        {
            var test = Path.Combine(store.Dir, $"write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(test, "");
            File.Delete(test);
            return (CheckStatus.OK, $"{store.Dir}（書き込めます）");
        });

        Add("持ち出し用辞書の保存先", () =>
        {
            var dir = Path.GetDirectoryName(store.BackupFile) ?? "";
            if (!Directory.Exists(dir)) return (CheckStatus.Warning, $"{store.BackupFile}（フォルダがありません。最初の登録で作ります）");
            var test = Path.Combine(dir, $"write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(test, "");
            File.Delete(test);
            return (CheckStatus.OK, $"{store.BackupFile}（書き込めます。登録済み {store.Registered.Count} 語）");
        });

        return list;
    }

    /// <summary>結果を貼り付けやすい文字列にする（問い合わせ・メモ用）</summary>
    public static string Format(IEnumerable<CheckResult> results, string version) =>
        $"YuWords {version} 環境チェック（{DateTime.Now:yyyy/MM/dd HH:mm}）\r\n" +
        $"{Environment.OSVersion.VersionString}、.NET {Environment.Version}\r\n" +
        string.Concat(results.Select(r => $"[{r.Status}] {r.Name}: {r.Detail}\r\n"));
}
