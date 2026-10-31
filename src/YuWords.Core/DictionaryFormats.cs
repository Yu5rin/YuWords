using System.Text;

namespace YuWords.Core;

/// <summary>書き出す辞書の形式</summary>
public enum DictFormat { MicrosoftIme, Google, Atok }

/// <summary>
/// ほかの日本語入力の辞書形式での書き出し・取り込み。
/// Microsoft IME: UTF-16 LE（BOM）、先頭行「!Microsoft IME Dictionary Tool」、よみ TAB 語句 TAB 品詞
/// Google 日本語入力: UTF-8、よみ TAB 語句 TAB 品詞 TAB コメント
/// ATOK: UTF-16 LE（BOM）、先頭行「!!ATOK_TANGO_TEXT_HEADER_1」、よみ TAB 語句 TAB 品詞（品詞名は ATOK の名前）
/// </summary>
public static class DictionaryFormats
{
    public const string AtokHeader = "!!ATOK_TANGO_TEXT_HEADER_1";

    /// <summary>YuWords の品詞名 → ATOK の品詞名</summary>
    static readonly Dictionary<string, string> ToAtok = new()
    {
        ["名詞"] = "名詞", ["人名"] = "固有人名", ["地名"] = "固有地名", ["固有名詞"] = "固有一般", ["短縮よみ"] = "短縮読み",
    };

    public static string Label(DictFormat f) => f switch
    {
        DictFormat.Google => "Google 日本語入力",
        DictFormat.Atok => "ATOK",
        _ => "Microsoft IME",
    };

    /// <summary>辞書を書き出す。よみの無い語は飛ばす。戻り値は書いた語数</summary>
    public static int Write(string path, IEnumerable<(string Reading, string Word, string Pos)> entries, DictFormat format)
    {
        if (format == DictFormat.MicrosoftIme) return OutputWriters.WriteImeText(path, entries);
        var enc = format == DictFormat.Google ? new UTF8Encoding(false) : (Encoding)new UnicodeEncoding(false, true);
        using var w = new StreamWriter(path, false, enc) { NewLine = "\r\n" };
        if (format == DictFormat.Atok) w.WriteLine(AtokHeader);
        int n = 0;
        foreach (var (r, word, pos) in entries)
        {
            if (r.Length == 0) continue;
            w.WriteLine(format == DictFormat.Google
                ? $"{r}\t{word}\t{pos}\t"
                : $"{r}\t{word}\t{ToAtok.GetValueOrDefault(pos, "名詞")}");
            n++;
        }
        return n;
    }

    /// <summary>
    /// 辞書を読む。Microsoft IME・Google 日本語入力・ATOK のテキスト形式と CSV を、中身と拡張子から見分ける。
    /// 品詞は YuWords の名前（名詞・人名・地名・固有名詞・短縮よみ）にそろえ、それ以外は名詞にする
    /// </summary>
    public static List<(string Reading, string Word, string Pos)> Read(string path)
    {
        if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return OutputWriters.ReadWordCsv(path);
        var text = DocumentSources.ReadText(path);
        var list = new List<(string, string, string)>();
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim('﻿');
            if (line.Length == 0 || line[0] == '!' || line[0] == '#') continue;
            var f = line.Split('\t');
            if (f.Length < 2) continue;
            var reading = TermExtractor.KataToHira(f[0].Trim());
            var word = f[1].Trim();
            if (reading.Length == 0 || word.Length == 0) continue;
            list.Add((reading, word, NormalizePos(f.Length >= 3 ? f[2].Trim() : "")));
        }
        return list;
    }

    /// <summary>各 IME の品詞名を YuWords の品詞名にそろえる</summary>
    public static string NormalizePos(string pos) => pos switch
    {
        "人名" or "固有人名" or "姓" or "名" or "姓名" => "人名",
        "地名" or "固有地名" => "地名",
        "固有名詞" or "固有一般" or "組織" or "固有組織" or "社名" => "固有名詞",
        "短縮よみ" or "短縮読み" => "短縮よみ",
        _ => "名詞",
    };
}
