using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace YuWords.Core;

/// <summary>
/// 人名のよみを、登録されたふりがな（Outlook の連絡先・会社のアドレス帳）で補う。
/// 形態素解析の推測（東海林→とうかいりん など）は外れやすいため
/// </summary>
public static class NameReadings
{
    static readonly Regex Hira = new(@"^[ぁ-んー]+$");

    /// <summary>表記 → よみ。同じ表記によみが2通り以上あるものは、どちらとも決められないので使わない</summary>
    public sealed class Map
    {
        readonly Dictionary<string, string> map = [];
        readonly HashSet<string> ambiguous = [];

        public int Count => map.Count;

        public void Add(string word, string reading)
        {
            if (ambiguous.Contains(word)) return;
            if (map.TryGetValue(word, out var r) && r != reading)
            {
                map.Remove(word);
                ambiguous.Add(word);
                return;
            }
            map[word] = reading;
        }

        public bool TryGetValue(string word, out string reading) => map.TryGetValue(word, out reading!);

        public void AddRange(Map other)
        {
            foreach (var (w, r) in other.map) Add(w, r);
            foreach (var w in other.ambiguous) { map.Remove(w); ambiguous.Add(w); }
        }
    }

    static string N(string? s) => MailPreprocessor.Normalize((s ?? "").Trim()).Replace(" ", "").Replace("　", "");

    /// <summary>姓・名とそのふりがなを足す（ふりがなはカタカナでもよい）。姓名（空白なし）も足す</summary>
    public static void Add(Map map, string? last, string? yomiLast, string? first, string? yomiFirst)
    {
        string Y(string? s) => TermExtractor.KataToHira(N(s));
        var (l, yl, f, yf) = (N(last), Y(yomiLast), N(first), Y(yomiFirst));
        bool okL = l.Length > 0 && Hira.IsMatch(yl);
        bool okF = f.Length > 0 && Hira.IsMatch(yf);
        if (okL) map.Add(l, yl);
        if (okF) map.Add(f, yf);
        if (okL && okF) map.Add(l + f, yl + yf);
    }

    /// <summary>Outlook クラシック版の既定の連絡先フォルダから作る。読めなければ空（取り込み自体は止めない）</summary>
    public static Map FromOutlookContacts(CancellationToken ct = default)
    {
        var map = new Map();
        const int OlFolderContacts = 10;
        const int OlContact = 40;
        try
        {
            if (Outlook() is not { } app) return map;
            dynamic items = app.GetNamespace("MAPI").GetDefaultFolder(OlFolderContacts).Items;
            foreach (dynamic item in items)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if ((int)item.Class != OlContact) continue;
                    Add(map, (string?)item.LastName, (string?)item.YomiLastName,
                             (string?)item.FirstName, (string?)item.YomiFirstName);
                }
                catch (Exception e) when (e is not OperationCanceledException) { }
                finally { Marshal.ReleaseComObject(item); }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // 連絡先が使えない環境では補わないだけ
        }
        return map;
    }

    // [MS-OXOABK] のアドレス帳のふりがな（Exchange の msDS-PhoneticLastName / FirstName）
    const string PhoneticSurname = "http://schemas.microsoft.com/mapi/proptag/0x8C8F001F";
    const string PhoneticGivenName = "http://schemas.microsoft.com/mapi/proptag/0x8C8E001F";

    /// <summary>
    /// 会社のアドレス帳（Exchange の GAL）から、targets の語で始まる名前の人のふりがなを集める。
    /// 大きなアドレス帳でも止まらないよう、件数と時間に上限を設ける。使えなければ空
    /// </summary>
    public static Map FromGlobalAddressList(IReadOnlyCollection<string> targets, CancellationToken ct = default,
        int maxEntries = 50000, int maxSeconds = 60)
    {
        var map = new Map();
        var names = targets.Select(N).Where(t => t.Length >= 2).Distinct().ToList();
        if (names.Count == 0) return map;
        try
        {
            if (Outlook() is not { } app) return map;
            dynamic entries = app.GetNamespace("MAPI").GetGlobalAddressList().AddressEntries;
            var sw = Stopwatch.StartNew();
            int count = (int)entries.Count;
            for (int i = 1; i <= Math.Min(count, maxEntries) && sw.Elapsed.TotalSeconds < maxSeconds; i++)
            {
                ct.ThrowIfCancellationRequested();
                dynamic? entry = null, user = null;
                try
                {
                    entry = entries[i];
                    var display = N((string?)entry.Name);
                    if (!names.Any(display.StartsWith)) continue;
                    user = entry.GetExchangeUser();
                    if (user == null) continue;
                    dynamic pa = user.PropertyAccessor;
                    Add(map, (string?)user.LastName, Prop(pa, PhoneticSurname), (string?)user.FirstName, Prop(pa, PhoneticGivenName));
                }
                catch (Exception e) when (e is not OperationCanceledException) { }
                finally
                {
                    if (user != null) Marshal.ReleaseComObject(user);
                    if (entry != null) Marshal.ReleaseComObject(entry);
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Exchange でない・オフラインなどの環境では補わないだけ
        }
        return map;
    }

    static string? Prop(dynamic pa, string tag)
    {
        try { return pa.GetProperty(tag) as string; }
        catch (Exception) { return null; }
    }

    static dynamic? Outlook()
    {
        var type = Type.GetTypeFromProgID("Outlook.Application");
        return type == null ? null : Activator.CreateInstance(type);
    }

    /// <summary>よみを補う対象（人名、または人名を含む要確認の語）</summary>
    public static List<string> Targets(IEnumerable<Candidate> rows) =>
        rows.Where(c => c.Category == "人名" || c.Unsure).Select(c => c.Word).ToList();

    /// <summary>候補のうち、辞書にある語のよみを置き換えて要確認を外す。戻り値は置き換えた数</summary>
    public static int Apply(List<Candidate> rows, Map map, string source = "連絡先")
    {
        if (map.Count == 0) return 0;
        int n = 0;
        foreach (var c in rows)
        {
            if (!map.TryGetValue(MailPreprocessor.Normalize(c.Word), out var yomi)) continue;
            c.Reading = yomi;
            c.Unsure = false;
            c.ReadingSource = source;
            n++;
        }
        return n;
    }
}
