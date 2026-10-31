using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace YuWords.Core;

/// <summary>
/// メール以外の取り込み元（Phase 4）。どれも「文書ごとの行リスト」を返し、
/// 以降はメールと同じ処理（定型行の分離・抽出・集計）にかける。出現数は「その語が出た文書の数」
/// </summary>
public static class DocumentSources
{
    // ===== 共通 =====

    /// <summary>テキストを行に分ける（前後の空白を除き、空行は捨てる）。メールと違い引用の判定はしない</summary>
    public static List<string> SplitLines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    /// <summary>BOM があればそれに従い、無ければ UTF-8 として読めるか試し、だめなら Shift_JIS として読む</summary>
    public static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF)
            || bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            using var r = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return r.ReadToEnd();
        }
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932).GetString(bytes);
        }
    }

    // ===== フォルダ（Obsidian Vault・テキスト） =====

    public static readonly string[] DefaultExtensions = [".md", ".txt", ".docx", ".pptx"];

    /// <summary>
    /// フォルダ以下の .md / .txt / .docx / .pptx を1ファイル1文書として読む。
    /// 隠しフォルダ（.obsidian・.git・.trash など）と Office の一時ファイル（~$）は飛ばす。Markdown は記法を外して本文だけにする。
    /// modifiedAfter を指定すると、それ以降に更新したファイルだけを読む
    /// </summary>
    public static List<List<string>> ReadFolder(string folder, IEnumerable<string>? extensions = null,
        IProgress<int>? progress = null, CancellationToken ct = default, DateTime? modifiedAfter = null)
    {
        var exts = new HashSet<string>(extensions ?? DefaultExtensions, StringComparer.OrdinalIgnoreCase);
        var docs = new List<List<string>>();
        foreach (var path in EnumerateFiles(folder, exts))
        {
            ct.ThrowIfCancellationRequested();
            if (Path.GetFileName(path).StartsWith("~$")) continue;
            if (modifiedAfter is { } after && File.GetLastWriteTime(path) < after) continue;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            string text;
            try
            {
                text = ext switch
                {
                    ".docx" => ReadDocx(path),
                    ".pptx" => ReadPptx(path),
                    _ => ReadText(path),
                };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                          or System.Xml.XmlException)
            {
                continue;   // 開けない・暗号化された・壊れたファイルは飛ばす
            }
            if (ext == ".md")
                text = StripMarkdown(text);
            // ファイル名（ノートの題名）も本文と同じく扱う
            var lines = SplitLines(text);
            lines.Insert(0, Path.GetFileNameWithoutExtension(path));
            docs.Add(lines);
            if (docs.Count % 50 == 0) progress?.Report(docs.Count);
        }
        progress?.Report(docs.Count);
        return docs;
    }

    static IEnumerable<string> EnumerateFiles(string folder, HashSet<string> exts)
    {
        var stack = new Stack<string>([folder]);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] files, dirs;
            try
            {
                files = Directory.GetFiles(dir);
                dirs = Directory.GetDirectories(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var f in files.Order(StringComparer.OrdinalIgnoreCase))
                if (exts.Contains(Path.GetExtension(f))) yield return f;
            foreach (var d in dirs.OrderDescending(StringComparer.OrdinalIgnoreCase))
                if (!Path.GetFileName(d).StartsWith('.')) stack.Push(d);
        }
    }

    static readonly Regex FrontMatter = new(@"\A---\n.*?\n---\n", RegexOptions.Singleline);
    static readonly Regex CodeFence = new(@"^(```|~~~).*?^\1[^\n]*$", RegexOptions.Singleline | RegexOptions.Multiline);
    static readonly Regex InlineCode = new(@"`[^`\n]*`");
    static readonly Regex Comment = new(@"(%%.*?%%|<!--.*?-->)", RegexOptions.Singleline);
    static readonly Regex Image = new(@"!\[\[[^\]]*\]\]|!\[[^\]]*\]\([^)]*\)");
    static readonly Regex WikiLink = new(@"\[\[([^\]|#]*)(?:#[^\]|]*)?(?:\|([^\]]*))?\]\]");
    static readonly Regex MdLink = new(@"\[([^\]]*)\]\([^)]*\)");
    static readonly Regex Url = new(@"https?://\S+");
    static readonly Regex TagHash = new(@"(?<=^|\s)#(?=[^\s#])", RegexOptions.Multiline);
    static readonly Regex LinePrefix = new(@"^\s*(#{1,6}\s+|>+\s?|[-*+]\s+\[.\]\s+|[-*+]\s+|\d+[.)]\s+|\|)", RegexOptions.Multiline);
    static readonly Regex Emphasis = new(@"(\*\*|__|==|~~|\*|_)(?=\S)(.+?)(?<=\S)\1");
    static readonly Regex TableRule = new(@"^\s*\|?\s*:?-{3,}.*$", RegexOptions.Multiline);

    /// <summary>Markdown（Obsidian 記法を含む）から記号を外し、本文だけにする</summary>
    public static string StripMarkdown(string md)
    {
        var s = md.ReplaceLineEndings("\n");
        s = FrontMatter.Replace(s, "");
        s = CodeFence.Replace(s, "");
        s = Comment.Replace(s, "");
        s = Image.Replace(s, "");
        s = WikiLink.Replace(s, m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value);
        s = MdLink.Replace(s, "$1");
        s = Url.Replace(s, "");
        s = InlineCode.Replace(s, "");
        s = TableRule.Replace(s, "");
        s = LinePrefix.Replace(s, "");
        s = TagHash.Replace(s, "");   // タグ（#設計審査）は自分で付けた語なので、# だけ外して本文に残す
        s = Emphasis.Replace(s, "$2");
        // 表の区切りは語の切れ目にする
        s = s.Replace('|', '\n');
        return s;
    }

    // ===== Word・PowerPoint =====

    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    static XDocument? LoadEntry(ZipArchive zip, string name)
    {
        var e = zip.GetEntry(name);
        if (e == null) return null;
        using var s = e.Open();
        return XDocument.Load(s);
    }

    static ZipArchive OpenZip(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            return new ZipArchive(fs, ZipArchiveMode.Read);
        }
        catch
        {
            fs.Dispose();   // zip でなかったときにファイルを開いたままにしない
            throw;
        }
    }

    /// <summary>Word（.docx）の本文を段落ごとの行にする。Word が無くても読める</summary>
    public static string ReadDocx(string path)
    {
        using var zip = OpenZip(path);
        var doc = LoadEntry(zip, "word/document.xml") ?? throw new InvalidDataException("Word ファイルとして読めませんでした。");
        return string.Join('\n', doc.Descendants(W + "p").Select(p =>
            string.Concat(p.Descendants().Select(x => x.Name == W + "t" ? x.Value : x.Name == W + "tab" ? " " : ""))));
    }

    /// <summary>PowerPoint（.pptx）のスライドの文字を段落ごとの行にする（スライド順）</summary>
    public static string ReadPptx(string path)
    {
        using var zip = OpenZip(path);
        var slides = zip.Entries
            .Where(e => Regex.IsMatch(e.FullName, @"^ppt/(slides/slide|notesSlides/notesSlide)\d+\.xml$"))
            .OrderBy(e => e.FullName.Contains("notes")).ThenBy(e => int.Parse(Regex.Match(e.Name, @"\d+").Value));
        var lines = new List<string>();
        foreach (var e in slides)
        {
            using var s = e.Open();
            var x = XDocument.Load(s);
            lines.AddRange(x.Descendants(A + "p").Select(p => string.Concat(p.Descendants(A + "t").Select(t => t.Value))));
        }
        return string.Join('\n', lines);
    }

    // ===== Excel・CSV の指定列 =====

    /// <summary>表の1シート分。Rows[行][列]</summary>
    public sealed record Sheet(string Name, List<string[]> Rows)
    {
        public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Length);
    }

    /// <summary>.xlsx / .csv / .tsv を読む。xlsx は全シート、csv は1シート</summary>
    public static List<Sheet> ReadTable(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".xlsx" or ".xlsm" => ReadXlsx(path),
            ".tsv" => [new Sheet(Path.GetFileNameWithoutExtension(path), ParseDelimited(ReadText(path), '\t'))],
            _ => [new Sheet(Path.GetFileNameWithoutExtension(path), ParseDelimited(ReadText(path), ','))],
        };
    }

    /// <summary>指定した列のセルを1つずつ文書にする。skipHeader なら1行目を飛ばす</summary>
    public static List<List<string>> ColumnDocuments(Sheet sheet, int column, bool skipHeader)
    {
        var docs = new List<List<string>>();
        foreach (var row in sheet.Rows.Skip(skipHeader ? 1 : 0))
        {
            if (column >= row.Length) continue;
            var lines = SplitLines(row[column]);
            if (lines.Count > 0) docs.Add(lines);
        }
        return docs;
    }

    static readonly Regex HeaderWord = new(@"^(用語|語句|単語|表記|見出し語|キーワード|読み|読み方|よみ|よみがな|ふりがな|フリガナ|読み仮名)$");
    static readonly Regex HiraReading = new(@"^[ぁ-んー]+$");
    static readonly Regex TrailingNote = new(@"(\s*\([^()]*\))+$");

    /// <summary>
    /// 見出しの行を探す（先頭 15 行のうち「用語」「読み方」などの見出しがある最初の行）。見つからなければ -1。
    /// 用語集は上に題名や注意書きの行があることが多いので、1行目とは限らない
    /// </summary>
    public static int FindHeaderRow(Sheet sheet)
    {
        for (int r = 0; r < Math.Min(sheet.Rows.Count, 15); r++)
            if (sheet.Rows[r].Any(c => HeaderWord.IsMatch(c.Trim())))
                return r;
        return -1;
    }

    /// <summary>見出しの行から、語句の列とよみの列を推し量る（無ければ -1）</summary>
    public static (int Word, int Reading) GuessGlossaryColumns(Sheet sheet, int headerRow)
    {
        if (headerRow < 0) return (-1, -1);
        var h = sheet.Rows[headerRow].Select(c => c.Trim()).ToArray();
        int word = Array.FindIndex(h, c => c is "用語" or "語句" or "単語" or "表記" or "見出し語" or "キーワード");
        int reading = Array.FindIndex(h, c => c is "読み" or "読み方" or "よみ" or "よみがな" or "ふりがな" or "フリガナ" or "読み仮名");
        return (word, reading);
    }

    /// <summary>
    /// 用語集として、語句とよみの組を取り出す（見出しの行より下）。
    /// 語句は全角英数などをそろえ、1行目だけを使う（60文字まで）。よみはひらがなにし、ひらがなにならなければ空にする
    /// </summary>
    public static List<(string Word, string Reading)> GlossaryEntries(Sheet sheet, int wordColumn, int readingColumn, int headerRow)
    {
        var list = new List<(string, string)>();
        var seen = new HashSet<string>();
        foreach (var row in sheet.Rows.Skip(headerRow + 1))
        {
            if (wordColumn >= row.Length) continue;
            var word = MailPreprocessor.Normalize(SplitLines(row[wordColumn]).FirstOrDefault() ?? "").Trim();
            // 末尾の括弧書きは説明なので外す（輸管(輸出管理) → 輸管）。外すと空になるなら、そのまま
            var bare = TrailingNote.Replace(word, "").Trim();
            if (bare.Length > 0) word = bare;
            if (word.Length == 0 || word.Length > 60 || !seen.Add(word)) continue;
            var reading = readingColumn >= 0 && readingColumn < row.Length
                ? TermExtractor.KataToHira(MailPreprocessor.Normalize(row[readingColumn]).Replace(" ", "").Replace("　", "").Trim())
                : "";
            list.Add((word, HiraReading.IsMatch(reading) ? reading : ""));
        }
        return list;
    }

    /// <summary>列番号（0始まり）を Excel の列名（A, B, ..., AA）にする</summary>
    public static string ColumnName(int index)
    {
        var s = "";
        for (index++; index > 0; index = (index - 1) / 26)
            s = (char)('A' + (index - 1) % 26) + s;
        return s;
    }

    /// <summary>CSV / TSV を読む（ダブルクォートで囲んだ値の中の区切り・改行・"" に対応）</summary>
    public static List<string[]> ParseDelimited(string text, char sep)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add([.. row]);
                row.Clear();
            }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add([.. row]);
        }
        return rows;
    }

    static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>xlsx を読む（値だけ。数式は計算結果のキャッシュ値）。Excel が無くても読める</summary>
    static List<Sheet> ReadXlsx(string path)
    {
        // Excel で開いている最中でも読めるように共有モードで開く
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        XDocument? Load(string name)
        {
            var e = zip.GetEntry(name);
            if (e == null) return null;
            using var s = e.Open();
            return XDocument.Load(s);
        }

        var shared = new List<string>();
        if (Load("xl/sharedStrings.xml") is { } sst)
            foreach (var si in sst.Root!.Elements(Main + "si"))
                shared.Add(string.Concat(si.Descendants(Main + "t").Where(t => t.Parent?.Name != Main + "rPh").Select(t => t.Value)));

        var rels = Load("xl/_rels/workbook.xml.rels")?.Root!.Elements(PkgRel + "Relationship")
                       .ToDictionary(r => (string)r.Attribute("Id")!, r => (string)r.Attribute("Target")!) ?? [];
        var sheets = new List<Sheet>();
        var wb = Load("xl/workbook.xml") ?? throw new InvalidDataException("Excel ファイルとして読めませんでした。");
        foreach (var s in wb.Root!.Element(Main + "sheets")?.Elements(Main + "sheet") ?? [])
        {
            var id = (string?)s.Attribute(Rel + "id");
            if (id == null || !rels.TryGetValue(id, out var target)) continue;
            target = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            if (Load(target) is not { } doc) continue;

            var rows = new List<string[]>();
            foreach (var r in doc.Descendants(Main + "row"))
            {
                var cells = new List<string>();
                foreach (var c in r.Elements(Main + "c"))
                {
                    int col = ColumnIndex((string?)c.Attribute("r")) ?? cells.Count;
                    while (cells.Count < col) cells.Add("");
                    var t = (string?)c.Attribute("t");
                    var v = (string?)c.Element(Main + "v") ?? "";
                    cells.Add(t switch
                    {
                        "s" when int.TryParse(v, out var i) && i < shared.Count => shared[i],
                        "inlineStr" => string.Concat(c.Descendants(Main + "t").Select(x => x.Value)),
                        _ => v,
                    });
                }
                int rowIndex = (int?)r.Attribute("r") is { } ri ? ri - 1 : rows.Count;
                while (rows.Count < rowIndex) rows.Add([]);
                rows.Add([.. cells]);
            }
            sheets.Add(new Sheet((string?)s.Attribute("name") ?? $"Sheet{sheets.Count + 1}", rows));
        }
        return sheets;
    }

    /// <summary>"C12" → 2（0始まりの列番号）</summary>
    static int? ColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return null;
        int n = 0, i = 0;
        for (; i < cellRef.Length && char.IsAsciiLetter(cellRef[i]); i++)
            n = n * 26 + (char.ToUpperInvariant(cellRef[i]) - 'A' + 1);
        return i == 0 ? null : n - 1;
    }
}
