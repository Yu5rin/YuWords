using System.Text;

namespace YuWords.Core;

/// <summary>チームの辞書の1語</summary>
/// <param name="Note">意味・メモ（空でもよい）</param>
public sealed record TeamWord(string Word, string Reading, string Pos, string AddedBy, string AddedAt, string Note = "");

/// <summary>
/// チームの辞書（社内の共有フォルダに置く CSV。語句・よみ・品詞・追加した人・追加日）。
/// 同僚と用語をそろえるため、みんなが語を足し、起動したときに新しい語を知らせる。
/// Excel でも開けるよう UTF-8（BOM付き）の CSV にする。同時に書き込まれても壊れないよう、書く直前に読み直して合わせる
/// </summary>
public static class TeamDictionary
{
    static readonly string[] Header = ["語句", "よみ", "品詞", "追加した人", "追加日", "意味"];

    /// <summary>読む。ファイルが無ければ空。見出しの名前で列を判断する（無ければ 1〜5 列目）</summary>
    public static List<TeamWord> Read(string path)
    {
        if (!File.Exists(path)) return [];
        var rows = DocumentSources.ParseDelimited(DocumentSources.ReadText(path), ',');
        if (rows.Count == 0) return [];
        int Col(string name, int fallback)
        {
            var i = Array.FindIndex(rows[0], h => h.Trim() == name);
            return i >= 0 ? i : fallback;
        }
        bool header = rows[0].Any(h => h.Trim() is "語句" or "よみ");
        int cw = Col("語句", 0), cr = Col("よみ", 1), cp = Col("品詞", 2), cb = Col("追加した人", 3), ca = Col("追加日", 4);
        int cn = header ? Array.FindIndex(rows[0], h => h.Trim() is "意味" or "意味・メモ" or "メモ") : 5;   // 前の版の CSV には意味の列が無い
        var list = new List<TeamWord>();
        var seen = new HashSet<(string, string)>();
        foreach (var r in rows.Skip(header ? 1 : 0))
        {
            string At(int i) => i < r.Length ? r[i].Trim() : "";
            var word = At(cw);
            var reading = TermExtractor.KataToHira(At(cr));
            if (word.Length == 0 || reading.Length == 0 || !seen.Add((word, reading))) continue;
            list.Add(new TeamWord(word, reading, DictionaryFormats.NormalizePos(At(cp)), At(cb), At(ca), cn >= 0 ? At(cn) : ""));
        }
        return list;
    }

    /// <summary>
    /// 語を足す（同じ語・よみがあれば足さない）。書く直前に読み直して合わせ、ほかの人が書いている最中なら少し待ってやり直す。
    /// 戻り値は足した数
    /// </summary>
    public static int Add(string path, IEnumerable<(string Word, string Reading, string Pos)> words, string user) =>
        Add(path, words.Select(w => (w.Word, w.Reading, w.Pos, "")), user);

    /// <summary>意味・メモ付きで足す。すでにある語でも、意味が空なら足した人の意味を入れる（足した数には数えない）</summary>
    public static int Add(string path, IEnumerable<(string Word, string Reading, string Pos, string Note)> words, string user)
    {
        var adding = words.Where(w => w.Word.Length > 0 && w.Reading.Length > 0).ToList();
        if (adding.Count == 0) return 0;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // ロック用のファイルを排他的に開いている間だけ「読む → 合わせる → 書く」を行う（共有フォルダでも 1 人ずつになる）
        var lockPath = path + ".lock";
        int replaceFailures = 0;
        for (int attempt = 0; ; attempt++)
        {
            FileStream? lockFile = null;
            string? tmp = null;
            try
            {
                lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                var current = Read(path);
                var have = current.Select(w => (w.Word, w.Reading)).ToHashSet();
                var today = DateTime.Now.ToString("yyyy/MM/dd");
                var added = adding.Where(w => have.Add((w.Word, w.Reading)))
                    .Select(w => new TeamWord(w.Word, w.Reading, w.Pos, user, today, w.Note.Trim())).ToList();
                // すでにある語で意味が空なら、足した人の意味を入れる
                var notes = adding.Where(w => w.Note.Trim().Length > 0).GroupBy(w => (w.Word, w.Reading)).ToDictionary(g => g.Key, g => g.First().Note.Trim());
                bool filled = false;
                for (int i = 0; i < current.Count; i++)
                    if (current[i].Note.Length == 0 && notes.TryGetValue((current[i].Word, current[i].Reading), out var note))
                        (current[i], filled) = (current[i] with { Note = note }, true);
                if (added.Count == 0 && !filled) return 0;
                tmp = path + $".{Guid.NewGuid():N}.tmp";
                Write(tmp, current.Concat(added));
                try { File.Move(tmp, path, true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // ロックは取れたのに置き換えられない＝誰かが Excel などで開いている。長く待っても開くまで閉じないので早めにあきらめる
                    if (++replaceFailures >= 5)
                        throw new IOException("チームの辞書を誰かが Excel などで開いているため、書き込めませんでした。閉じてもらってから、もう一度足してください。", e);
                    throw;
                }
                tmp = null;
                return added.Count;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 40 && replaceFailures < 5)
            {
                Thread.Sleep(100 + 50 * Math.Min(attempt, 10));   // ほかの人が書いている（最大でおよそ 20 秒待つ）
            }
            finally
            {
                if (tmp != null) try { File.Delete(tmp); } catch (Exception) { }   // 置き換えられなかった書きかけを残さない
                lockFile?.Dispose();
            }
        }
    }

    static void Write(string path, IEnumerable<TeamWord> words)
    {
        static string Q(string s) => s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Header)).Append("\r\n");
        foreach (var w in words)
            sb.Append(string.Join(",", new[] { w.Word, w.Reading, w.Pos, w.AddedBy, w.AddedAt, w.Note }.Select(Q))).Append("\r\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    /// <summary>まだ知らせていない新しい語（自分が登録済み・除外した・すでに見た語は除く）</summary>
    public static List<TeamWord> NewWords(IEnumerable<TeamWord> team, AppStore store) =>
        team.Where(w => !store.TeamSeen.Contains(Key(w.Word, w.Reading))
                        && !store.Excluded.Contains(w.Word)
                        && !store.Registered.Any(r => r.Word == w.Word && r.Reading == w.Reading)).ToList();

    public static string Key(string word, string reading) => $"{word}\t{reading}";
}
