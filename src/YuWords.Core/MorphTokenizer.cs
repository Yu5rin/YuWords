using NMeCab;
using NMeCab.Specialized;

namespace YuWords.Core;

/// <summary>形態素1つ分。品詞は UniDic の体系（Sudachi と同じ並び）</summary>
public sealed record Token(string Surface, string Pos1, string Pos2, string Pos3, string? Kana, bool IsUnknown);

/// <summary>NMeCab ＋ UniDic による形態素解析</summary>
public sealed class MorphTokenizer : IDisposable
{
    readonly MeCabUniDic21Tagger tagger;

    public MorphTokenizer(string? dicDir = null)
    {
        dicDir ??= FindDictionary();
        tagger = MeCabUniDic21Tagger.Create(dicDir);
    }

    /// <summary>実行ファイルの隣、またはその親をたどって dict/unidic を探す</summary>
    public static string FindDictionary()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "dict", "unidic");
            if (File.Exists(Path.Combine(path, "sys.dic")))
                return path;
        }
        throw new FileNotFoundException(
            "辞書（dict\\unidic\\sys.dic）が見つかりません。tools\\get-dictionary.ps1 で取得してください。");
    }

    static readonly Token Space = new(" ", "空白", "", "", null, false);

    /// <summary>
    /// 形態素に分ける。MeCab は空白を読み飛ばして前後をつなげてしまうので、
    /// 空白で区切って解析し、間に「空白」トークンを挟む（Sudachi と同じ扱い）
    /// </summary>
    public List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        foreach (var seg in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (tokens.Count > 0) tokens.Add(Space);
            foreach (var n in tagger.Parse(seg))
            {
                tokens.Add(new Token(n.Surface, n.Pos1 ?? "", n.Pos2 ?? "", n.Pos3 ?? "",
                                     n.Kana, n.Stat == MeCabNodeStat.Unk));
            }
        }
        return tokens;
    }

    public void Dispose() => tagger.Dispose();
}
