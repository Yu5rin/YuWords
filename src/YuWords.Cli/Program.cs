using System.Text;
using YuWords.Core;

namespace YuWords.Cli;

/// <summary>
/// Phase 1 の動作確認用コンソール。
///   YuWords.Cli outlook [--days N] [--max N] [--min N] [--out DIR]   … Outlook の送信済みメールから抽出
///   YuWords.Cli file &lt;テキスト&gt; [--min N] [--out DIR]              … 「---」だけの行で区切ったテキストをメール群とみなして抽出
///   YuWords.Cli folder &lt;フォルダ&gt; [--min N] [--out DIR]           … フォルダ以下の .md / .txt を1ファイル1文書として抽出（Obsidian の Vault など）
///   YuWords.Cli table &lt;xlsx|csv&gt; &lt;列名&gt; [--sheet 名前] [--no-header] … 表の指定列のセルを1つずつ文書として抽出
///   YuWords.Cli terms &lt;テキスト&gt;                                   … 1行ずつ抽出結果を表示（集計なし）
///   YuWords.Cli tokens &lt;文&gt;                                        … 形態素解析の結果を表示
///   YuWords.Cli diagnose                                          … 辞書・Outlook・IME が使えるかをまとめて確かめる（登録はしない）
///   YuWords.Cli ime-probe                                         … IME の変換エンジンとユーザー辞書を開けるかだけ確かめる（登録はしない）
/// </summary>
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0)
        {
            Console.WriteLine("使い方: YuWords.Cli outlook|file|folder|table|terms|tokens|ime-probe ...");
            return 1;
        }
        try
        {
            var settings = new ExtractorSettings();
            var outDir = ".";
            var rest = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--days": settings.Days = int.Parse(args[++i]); break;
                    case "--max": settings.MaxMails = int.Parse(args[++i]); break;
                    case "--min": settings.MinMails = int.Parse(args[++i]); break;
                    case "--out": outDir = args[++i]; break;
                    case "--sheet": rest.Add(args[i]); rest.Add(args[++i]); break;
                    default: rest.Add(args[i]); break;
                }
            }

            switch (args[0])
            {
                case "tokens": return Tokens(string.Join(" ", rest));
                case "ime-probe": return ImeProbe();
                case "ime-selftest":
                    // 架空の語を登録・読み直し・削除して、直接登録が効くかを確かめる
                    Console.WriteLine(ImeRegistration.SelfTest());
                    return 0;
                case "update-check":
                {
                    // update-check [今の版] … GitHub に新しい版があるかを確かめる（取ってはこない）
                    var current = rest.Count > 0 ? Version.Parse(rest[0]) : Updater.CurrentVersion;
                    var info = Updater.CheckGitHubAsync(current).GetAwaiter().GetResult();
                    if (info != null && args.Contains("--download"))
                    {
                        // 取ってきて SHA256 を確かめ、展開するところまで（差し替えはしない。確認用の作業フォルダに）
                        Updater.UpdateDir = Path.Combine(Path.GetTempPath(), "YuWords-update-check");
                        var zip = Updater.DownloadAsync(info).GetAwaiter().GetResult();
                        var staged = Updater.Extract(zip, info.Version);
                        Console.WriteLine($"取ってきて確かめました（SHA256 一致）: {zip}\n展開先: {staged}");
                    }
                    Console.WriteLine(info == null ? $"新しい版はありません（v{current}）"
                        : $"新しい版 v{info.Version}\n  {info.Location}\n  {info.Size:N0} バイト\n  SHA256 {info.Sha256 ?? "不明"}\n{info.Notes}");
                    return 0;
                }
                case "diagnose":
                {
                    var results = Diagnostics.Run(new AppStore());
                    Console.Write(Diagnostics.Format(results, typeof(Program).Assembly.GetName().Version?.ToString() ?? ""));
                    return results.Any(r => r.Status == CheckStatus.NG) ? 1 : 0;
                }
                case "phonetic":
                {
                    using var conv = new ImeConverter();
                    foreach (var w in rest) Console.WriteLine($"{w}	{conv.Phonetic(w)}");
                    return 0;
                }
                case "terms": return Terms(rest[0], settings);
                case "file": return Run(MailTextSource.Read(rest[0]), settings, outDir);
                case "folder": return Run(DocumentSources.ReadFolder(rest[0]), settings, outDir);   // .md / .txt / .docx / .pptx
                case "table":
                {
                    // table <ファイル> <列名 A,B,...> [--sheet 名前] [--no-header]
                    var sheets = DocumentSources.ReadTable(rest[0]);
                    var sheetName = rest.SkipWhile(a => a != "--sheet").Skip(1).FirstOrDefault();
                    var sheet = sheetName == null ? sheets[0] : sheets.First(s => s.Name == sheetName);
                    int col = Enumerable.Range(0, Math.Max(sheet.ColumnCount, 1))
                        .First(c => DocumentSources.ColumnName(c).Equals(rest[1], StringComparison.OrdinalIgnoreCase));
                    return Run(DocumentSources.ColumnDocuments(sheet, col, !rest.Contains("--no-header")), settings, outDir);
                }
                case "outlook":
                    Console.WriteLine("送信メールを読み込み中...");
                    var mails = OutlookMailSource.FetchSentMails(settings.Days, settings.MaxMails,
                        new SyncProgress<int>(n => Console.Write($"\r  {n} 件")));
                    Console.WriteLine();
                    return Run(mails, settings, outDir);
                default:
                    Console.WriteLine($"不明なコマンド: {args[0]}");
                    return 1;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"エラー: {e.Message}");
            return 2;
        }
    }

    static int Tokens(string text)
    {
        using var tok = new MorphTokenizer();
        foreach (var t in tok.Tokenize(MailPreprocessor.Normalize(text)))
            Console.WriteLine($"{t.Surface}\t{t.Pos1},{t.Pos2},{t.Pos3}\t{t.Kana}\t{(t.IsUnknown ? "未知語" : "")}");
        return 0;
    }

    /// <summary>IME のユーザー辞書を開けるか、品詞番号が取れるかだけを確かめる（何も登録しない）</summary>
    static int ImeProbe()
    {
        using (var conv = new ImeConverter())
            Console.WriteLine(conv.IsAvailable ? $"IFELanguage: OK（ひんしつほしょうか → {conv.Convert("ひんしつほしょうか")}）" : $"IFELanguage: NG（{conv.Error}）");
        try
        {
            using var dic = ImeDictionary.Open();
            Console.WriteLine("IFEDictionary: ユーザー辞書を開けました");
            foreach (var (name, n) in ImeDictionary.PosNumbers) Console.WriteLine($"  {name} = {n}");
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine($"IFEDictionary: NG（{e.Message}）");
            return 1;
        }
        Console.WriteLine($"ユーザー辞書ツール: {ImeDictionary.DictionaryToolPath() ?? "見つかりません"}");
        return 0;
    }

    static int Terms(string path, ExtractorSettings settings)
    {
        using var tok = new MorphTokenizer();
        var ex = new TermExtractor(tok, settings);
        foreach (var line in File.ReadAllLines(path))
        {
            var terms = ex.Extract(line).Select(x => x.Term)
                .Select(t => $"({t.Word}, {t.Reading}, {(t.Unsure ? "要確認" : "")}, {t.Category}, {(t.IsCommon ? "一般" : "")})");
            Console.WriteLine($"{line} => [{string.Join(", ", terms)}]");
        }
        return 0;
    }

    static int Run(List<List<string>> mails, ExtractorSettings settings, string outDir)
    {
        Console.WriteLine($"  {mails.Count} 件読み込み");
        if (mails.Count == 0)
        {
            Console.WriteLine("読み込める文書がありませんでした。");
            return 1;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rows = Extraction.Run(mails, settings, progress: new SyncProgress<string>(Console.WriteLine));
        Console.WriteLine($"  {sw.Elapsed.TotalSeconds:F1} 秒");

        Directory.CreateDirectory(outDir);
        void Notify(string m) => Console.WriteLine($"  [注意] {m}");
        var csvPath = OutputWriters.WritablePath(Path.Combine(outDir, "candidates.csv"), Notify);
        var okPath = OutputWriters.WritablePath(Path.Combine(outDir, "ime_import.txt"), Notify);
        var chkPath = OutputWriters.WritablePath(Path.Combine(outDir, "ime_import_要確認.txt"), Notify);

        OutputWriters.WriteCsv(csvPath, rows);
        int nOk = OutputWriters.WriteImeText(okPath,
            rows.Where(c => !c.Unsure).Select(c => (c.Reading, c.Word, "名詞")));
        int nChk = OutputWriters.WriteImeText(chkPath,
            rows.Where(c => c.Unsure).Select(c => (c.Reading, c.Word, "名詞")));

        Console.WriteLine($"完了: {csvPath}（{rows.Count}語）、{okPath}（{nOk}語）、{chkPath}（{nChk}語）を出力しました。");
        return 0;
    }

    /// <summary>Progress&lt;T&gt; はスレッドプールで呼ぶため、コンソールでは順序が乱れる。その場で呼ぶ版</summary>
    sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
