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
        // Excel などで開かれていても読めるよう、書き込み中・置き換え中のファイルも共有して開く
        byte[] bytes;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var ms = new MemoryStream())
        {
            fs.CopyTo(ms);
            bytes = ms.ToArray();
        }
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

    /// <summary>
    /// 読む文書の種類。.vtt は Teams 会議の文字起こし、.html・.htm は Teams のチャットなどを保存したもの、
    /// .mht・.mhtml は OneNote の「単一ファイル Web ページ」でエクスポートしたもの
    /// </summary>
    public static readonly string[] DefaultExtensions = [".md", ".txt", ".docx", ".pptx", ".vtt", ".html", ".htm", ".mht", ".mhtml"];

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
            if (ReadDocument(path) is not { } lines) continue;   // 開けない・暗号化された・壊れたファイルは飛ばす
            docs.Add(lines);
            if (docs.Count % 50 == 0) progress?.Report(docs.Count);
        }
        progress?.Report(docs.Count);
        return docs;
    }

    /// <summary>貼り付けた文章を、空行で区切られたまとまり（段落）ごとの文書にする</summary>
    public static List<List<string>> Paragraphs(string text)
    {
        var docs = new List<List<string>>();
        var cur = new List<string>();
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0) { cur.Add(line); continue; }
            if (cur.Count > 0) { docs.Add(cur); cur = []; }
        }
        if (cur.Count > 0) docs.Add(cur);
        return docs;
    }

    /// <summary>
    /// 1つのファイルを、ファイル名（ノートの題名）を先頭にした行のリストとして読む。開けない・壊れているときは null
    /// </summary>
    public static List<string>? ReadDocument(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        string text;
        try
        {
            text = ext switch
            {
                ".docx" => ReadDocx(path),
                ".pptx" => ReadPptx(path),
                ".md" => StripMarkdown(ReadText(path)),
                ".vtt" => StripVtt(ReadText(path)),
                ".html" or ".htm" => HtmlToText(ReadText(path)),
                ".mht" or ".mhtml" => ReadMht(path),
                _ => ReadText(path),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                      or System.Xml.XmlException or FormatException)
        {
            return null;
        }
        var lines = SplitLines(text);
        lines.Insert(0, Path.GetFileNameWithoutExtension(path));
        return lines;
    }

    static readonly Regex VttVoice = new(@"<v(?:\.[^ >]*)?\s+([^>]+)>", RegexOptions.Compiled);
    static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>
    /// WebVTT（Teams 会議の文字起こし）から話した内容を取り出す。時刻の行・番号の行・NOTE は捨て、
    /// 話した人の名前（&lt;v 山田 太郎&gt;）は人名の候補になるよう、内容とは別の行にする
    /// </summary>
    public static string StripVtt(string vtt)
    {
        var sb = new StringBuilder();
        bool note = false;
        string? lastSpeaker = null;
        foreach (var raw in vtt.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { note = false; continue; }
            if (note || line.StartsWith("WEBVTT") || line.Contains("-->") || line.All(char.IsDigit)) continue;
            if (line.StartsWith("NOTE")) { note = true; continue; }
            // Teams は番号の代わりに「GUID/数字」の行を入れる
            if (Regex.IsMatch(line, @"^[0-9a-fA-F-]{20,}(/\d+(-\d+)?)?$")) continue;
            if (VttVoice.Match(line) is { Success: true } v)
            {
                var speaker = v.Groups[1].Value.Trim();
                if (speaker != lastSpeaker) sb.Append(speaker).Append('\n');
                lastSpeaker = speaker;
            }
            sb.Append(System.Net.WebUtility.HtmlDecode(Tag.Replace(line, ""))).Append('\n');
        }
        return sb.ToString();
    }

    static readonly Regex HtmlDrop = new(@"<(script|style|head)\b.*?</\1\s*>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    static readonly Regex HtmlBreak = new(@"<(br|/p|/div|/li|/tr|/h[1-6]|/td|/th|/title)\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>HTML から文字だけを取り出す（段落・改行・表のセルは行に分ける）</summary>
    public static string HtmlToText(string html)
    {
        var s = HtmlDrop.Replace(html, " ");
        s = HtmlBreak.Replace(s, "\n");
        s = Tag.Replace(s, " ");
        return System.Net.WebUtility.HtmlDecode(s).Replace('\u00a0', ' ');
    }

    /// <summary>
    /// MHT（OneNote などの「単一ファイル Web ページ」）から、HTML の部分を取り出して文字にする。
    /// quoted-printable と base64 の両方と、Content-Type の charset に対応する
    /// </summary>
    public static string ReadMht(string path)
    {
        var raw = File.ReadAllText(path, Encoding.Latin1);   // まずバイトのまま読み、部分ごとに文字コードを当てる
        return MhtToText(raw);
    }

    internal static string MhtToText(string raw)
    {
        var text = new StringBuilder();
        foreach (var part in MimeParts(raw))
        {
            var (headers, body) = part;
            var type = Header(headers, "Content-Type");
            if (!type.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) && !type.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase)) continue;
            var bytes = Header(headers, "Content-Transfer-Encoding").ToLowerInvariant() switch
            {
                "quoted-printable" => DecodeQuotedPrintable(body),
                "base64" => Convert.FromBase64String(Regex.Replace(body, @"\s", "")),
                _ => Encoding.Latin1.GetBytes(body),
            };
            var charset = Regex.Match(type, @"charset=""?([^"";\s]+)", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : "utf-8";
            Encoding enc;
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                enc = Encoding.GetEncoding(charset);
            }
            catch (ArgumentException) { enc = Encoding.UTF8; }
            var s = enc.GetString(bytes);
            text.Append(type.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ? HtmlToText(s) : s).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>MIME の部分（ヘッダーと本文）。multipart でなければ全体を1つの部分とする</summary>
    static IEnumerable<(string Headers, string Body)> MimeParts(string raw)
    {
        raw = raw.ReplaceLineEndings("\n");
        int split = raw.IndexOf("\n\n", StringComparison.Ordinal);
        if (split < 0) yield break;
        var top = raw[..split];
        var boundary = Regex.Match(top, @"boundary=""?([^"";\n]+)""?", RegexOptions.IgnoreCase);
        if (!boundary.Success)
        {
            yield return (top, raw[(split + 2)..]);
            yield break;
        }
        foreach (var chunk in raw.Split("--" + boundary.Groups[1].Value).Skip(1))
        {
            if (chunk.StartsWith("--")) break;   // 終わりの区切り
            var c = chunk.TrimStart('\n');
            int sep = c.IndexOf("\n\n", StringComparison.Ordinal);
            if (sep < 0) continue;
            yield return (c[..sep], c[(sep + 2)..]);
        }
    }

    static string Header(string headers, string name)
    {
        // 折り返された行（次の行が空白で始まる）をつなげてから探す
        var unfolded = Regex.Replace(headers, @"\n[ \t]+", " ");
        var m = Regex.Match(unfolded, $@"^{Regex.Escape(name)}:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    static byte[] DecodeQuotedPrintable(string s)
    {
        var bytes = new List<byte>(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '=' && i + 1 < s.Length && s[i + 1] == '\n') { i++; continue; }   // 行の折り返し
            if (s[i] == '=' && i + 2 < s.Length && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]))
            {
                bytes.Add(Convert.ToByte(s.Substring(i + 1, 2), 16));
                i += 2;
                continue;
            }
            bytes.Add((byte)s[i]);
        }
        return bytes.ToArray();
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

    /// <summary>見出しの行から、意味・説明の列を推し量る（「意味/定義」「説明」「備考」など。無ければ -1）</summary>
    public static int GuessNoteColumn(Sheet sheet, int headerRow)
    {
        if (headerRow < 0) return -1;
        var h = sheet.Rows[headerRow].Select(c => c.Trim()).ToArray();
        int i = Array.FindIndex(h, c => Regex.IsMatch(c, "意味|定義|説明|解説"));
        return i >= 0 ? i : Array.FindIndex(h, c => c is "備考" or "メモ");
    }

    /// <summary>用語集の1語</summary>
    /// <param name="Note">意味・説明（例文の欄に出す）</param>
    /// <param name="Adjusted">語句を自動で直した（「MOP 遠隔発報装置」→「MOP」など）。確かめてもらうため要確認にする</param>
    /// <param name="Extra">本来のよみのほかに付けるよみ（英字の語の英字読み など）</param>
    public sealed record GlossaryEntry(string Word, string Reading, string Note = "", bool Adjusted = false, string Extra = "");

    static readonly Regex AsciiHeadJapaneseTail = new(@"^([\x21-\x7E]+)\s+(.*[ぁ-んァ-ヶ一-龥].*)$");
    static readonly Regex AsciiOnlyWord = new(@"^[A-Za-z0-9\-_]+$");

    /// <summary>
    /// 用語集の1語の語句を整える。末尾の括弧書きは外す。「英字の略語＋空白＋日本語の説明」（MOP 遠隔発報装置）は語句を英字だけにして、
    /// 説明は意味に回す。英字だけの語で、よみが英字読みと違うなら、英字読みも別のよみとして付ける
    /// </summary>
    public static GlossaryEntry CleanGlossaryEntry(string word, string reading, string note)
    {
        var bare = TrailingNote.Replace(word, "").Trim();
        if (bare.Length > 0) word = bare;
        bool adjusted = false;
        if (AsciiHeadJapaneseTail.Match(word) is { Success: true } m)
        {
            note = note.Length > 0 ? $"{m.Groups[2].Value.Trim()}。{note}" : m.Groups[2].Value.Trim();
            word = m.Groups[1].Value;
            adjusted = true;
        }
        var extra = "";
        if (AsciiOnlyWord.IsMatch(word) && TermExtractor.AlphabetReading(word) is { } alpha && reading.Length > 0 && alpha != reading)
            extra = alpha;
        return new GlossaryEntry(word, reading, note, adjusted, extra);
    }

    /// <summary>
    /// 用語集として、語句とよみの組を取り出す（見出しの行より下）。
    /// 語句は全角英数などをそろえ、1行目だけを使う（60文字まで）。よみはひらがなにし、ひらがなにならなければ空にする
    /// </summary>
    public static List<GlossaryEntry> GlossaryEntries(Sheet sheet, int wordColumn, int readingColumn, int headerRow, int noteColumn = -1)
    {
        var list = new List<GlossaryEntry>();
        var seen = new HashSet<string>();
        foreach (var row in sheet.Rows.Skip(headerRow + 1))
        {
            if (wordColumn >= row.Length) continue;
            var word = MailPreprocessor.Normalize(SplitLines(row[wordColumn]).FirstOrDefault() ?? "").Trim();
            var reading = readingColumn >= 0 && readingColumn < row.Length
                ? TermExtractor.KataToHira(MailPreprocessor.Normalize(row[readingColumn]).Replace(" ", "").Replace("　", "").Trim())
                : "";
            var note = noteColumn >= 0 && noteColumn < row.Length ? string.Join(" ", SplitLines(row[noteColumn])) : "";
            var e = CleanGlossaryEntry(word, HiraReading.IsMatch(reading) ? reading : "", note);
            if (e.Word.Length == 0 || e.Word.Length > 60 || !seen.Add(e.Word)) continue;
            list.Add(e);
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
