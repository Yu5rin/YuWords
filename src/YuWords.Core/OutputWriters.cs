using System.Text;

namespace YuWords.Core;

/// <summary>CSV・IME取り込み用テキストの書き出し</summary>
public static class OutputWriters
{
    public const string ImeHeader = "!Microsoft IME Dictionary Tool";

    /// <summary>全候補を UTF-8（BOM付き）の CSV に書く（Excel 確認用）</summary>
    public static void WriteCsv(string path, IEnumerable<Candidate> rows) =>
        WriteCsv(path, ["語句", "よみ(推測)", "出現メール数", "分類", "署名", "IMEの変換結果", "IMEで変換可", "よみ要確認"],
            rows.Select(c => new[]
            {
                c.Word, c.Reading, c.MailCount.ToString(), c.Category, c.FromSignature ? "署名" : "",
                c.ImeConversion, c.ImeConvertible ? "○" : "", c.Unsure ? "要確認" : "",
            }));

    /// <summary>確認画面の行を UTF-8（BOM付き）の CSV に書く（編集後のよみ・品詞・登録チェックを含む）</summary>
    public static void WriteCsv(string path, IEnumerable<ReviewItem> rows) =>
        WriteCsv(path, ["登録", "語句", "よみ", "別のよみ", "品詞", "出現数", "分類", "署名・定型", "IMEのよみ", "IMEの変換結果", "IMEで変換可", "よみ要確認", "登録済み"],
            rows.Select(i => new[]
            {
                i.Register ? "○" : "", i.Word, i.Reading, string.Join("、", i.ExtraReadings), i.Pos, i.MailCount.ToString(), i.Category,
                i.FromSignature ? "○" : "", i.ImeReading, i.ImeConversion, i.ImeConvertible ? "○" : "",
                i.Unsure ? "要確認" : "", i.IsRegistered ? "○" : "",
            }));

    static void WriteCsv(string path, string[] header, IEnumerable<string[]> rows)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(true));
        w.NewLine = "\r\n";
        w.WriteLine(string.Join(",", header));
        foreach (var r in rows)
            w.WriteLine(string.Join(",", r.Select(Quote)));
    }

    static string Quote(string s) =>
        s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    /// <summary>
    /// Microsoft IME ユーザー辞書ツールの「テキストファイルから登録」形式で書く。
    /// UTF-16 LE（BOM付き）、CRLF、1行目にヘッダ、以降「よみ TAB 語句 TAB 品詞」。戻り値は書いた語数
    /// </summary>
    public static int WriteImeText(string path, IEnumerable<(string Reading, string Word, string Pos)> entries)
    {
        using var w = new StreamWriter(path, false, new UnicodeEncoding(false, true));
        w.NewLine = "\r\n";
        w.WriteLine(ImeHeader);
        int n = 0;
        foreach (var (r, word, pos) in entries)
        {
            if (r.Length == 0) continue;
            w.WriteLine($"{r}\t{word}\t{pos}");
            n++;
        }
        return n;
    }

    /// <summary>
    /// IME 取り込み形式のテキストを読む（持ち出し用辞書の取り込み用）。
    /// BOM で文字コードを判定し、BOM が無ければ UTF-8 として読む。コメント行（!）と不正な行は飛ばす
    /// </summary>
    public static List<(string Reading, string Word, string Pos)> ReadImeText(string path)
    {
        var list = new List<(string, string, string)>();
        foreach (var raw in File.ReadAllLines(path, new UTF8Encoding(false)))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '!') continue;
            var f = line.Split('\t');
            if (f.Length < 2 || f[0].Trim().Length == 0 || f[1].Trim().Length == 0) continue;
            list.Add((f[0].Trim(), f[1].Trim(), f.Length >= 3 && f[2].Trim().Length > 0 ? f[2].Trim() : "名詞"));
        }
        return list;
    }

    /// <summary>登録済みの語を CSV（語句・よみ・品詞・登録日）に書く</summary>
    public static void WriteRegisteredCsv(string path, IEnumerable<RegisteredWord> words) =>
        WriteCsv(path, ["語句", "よみ", "品詞", "登録日"],
            words.Select(w => new[] { w.Word, w.Reading, w.Pos, w.RegisteredAt.ToString("yyyy/MM/dd HH:mm") }));

    /// <summary>
    /// 語の一覧を CSV から読む。見出し行に「語句」「よみ」「品詞」があればその列を、無ければ 1〜3 列目を使う。
    /// 品詞が無い・空の行は名詞
    /// </summary>
    public static List<(string Reading, string Word, string Pos)> ReadWordCsv(string path)
    {
        var rows = DocumentSources.ParseDelimited(DocumentSources.ReadText(path), ',');
        if (rows.Count == 0) return [];
        int Find(string[] names, int fallback)
        {
            var i = Array.FindIndex(rows[0], h => names.Contains(h.Trim()));
            return i >= 0 ? i : fallback;
        }
        bool hasHeader = rows[0].Any(h => h.Trim() is "語句" or "よみ" or "読み" or "品詞");
        int cw = Find(["語句", "単語", "表記"], 0), cr = Find(["よみ", "読み"], 1);
        // 見出しがあるのに「品詞」の列が無ければ、品詞は使わない（ほかの列を品詞と取り違えない）
        int cp = hasHeader ? Array.FindIndex(rows[0], h => h.Trim() == "品詞") : 2;
        var list = new List<(string, string, string)>();
        foreach (var r in rows.Skip(hasHeader ? 1 : 0))
        {
            string At(int i) => i < r.Length ? r[i].Trim() : "";
            var word = At(cw);
            var reading = TermExtractor.KataToHira(At(cr));
            if (word.Length == 0 || reading.Length == 0) continue;
            list.Add((reading, word, cp >= 0 ? DictionaryFormats.NormalizePos(At(cp)) : "名詞"));
        }
        return list;
    }

    /// <summary>IME 取り込み形式のテキストか CSV（拡張子で判断）から語の一覧を読む</summary>
    public static List<(string Reading, string Word, string Pos)> ReadWords(string path) =>
        Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? ReadWordCsv(path) : ReadImeText(path);

    /// <summary>ファイルが Excel 等で開かれていて書けない場合は、日時付きの別名にする</summary>
    public static string WritablePath(string path, Action<string>? notify = null)
    {
        try
        {
            using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None)) { }
            return path;
        }
        catch (IOException)
        {
            var dir = Path.GetDirectoryName(path) ?? "";
            var alt = Path.Combine(dir,
                $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(path)}");
            notify?.Invoke($"{Path.GetFileName(path)} が開かれているため {Path.GetFileName(alt)} に保存します");
            return alt;
        }
    }
}
