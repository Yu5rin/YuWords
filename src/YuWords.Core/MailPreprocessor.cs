using System.Text;
using System.Text.RegularExpressions;

namespace YuWords.Core;

/// <summary>引用除去・署名分離・文字正規化</summary>
public static class MailPreprocessor
{
    static readonly Regex[] QuoteStart =
    [
        new(@"^-{2,}\s*(Original Message|元のメッセージ)", RegexOptions.IgnoreCase),
        new(@"^_{5,}$"),
        new(@"^(From|差出人|送信者|送信日時|Sent)\s*[:：]", RegexOptions.IgnoreCase),
        new(@"^On .+wrote:$"),
        new(@"^\d{4}[/年].*(wrote|書きました)"),
    ];
    static readonly Regex QuoteLine = new(@"^\s*(>|＞)");

    /// <summary>引用・転送部分と空行を除いた行のリスト</summary>
    public static List<string> StripQuote(string body)
    {
        var lines = new List<string>();
        foreach (var line in body.ReplaceLineEndings("\n").Split('\n'))
        {
            var s = line.Trim();
            if (QuoteStart.Any(p => p.IsMatch(s)))
                break;
            if (QuoteLine.IsMatch(line) || s.Length == 0)
                continue;
            lines.Add(s);
        }
        return lines;
    }

    /// <summary>
    /// 署名・定型文の行を分離する。
    /// 戻り値: (本文リスト, {共通行: 出現メール数})
    /// </summary>
    public static (List<string> Bodies, Dictionary<string, int> Common) SplitCommonLines(
        IReadOnlyList<List<string>> mails, double ratio)
    {
        var df = new Dictionary<string, int>();
        foreach (var lines in mails)
            foreach (var l in lines.Distinct())
                df[l] = df.GetValueOrDefault(l) + 1;
        int limit = Math.Max(3, (int)(mails.Count * ratio));
        var common = df.Where(kv => kv.Value >= limit).ToDictionary(kv => kv.Key, kv => kv.Value);
        var bodies = mails.Select(lines => string.Join("\n", lines.Where(l => !common.ContainsKey(l)))).ToList();
        return (bodies, common);
    }

    /// <summary>全角英数・半角カナを統一（ＸＹＺ→XYZ、ﾚｰﾙ→レール、シ゛→ジ）</summary>
    public static string Normalize(string text)
    {
        text = text.Replace('゛', '゙').Replace('゜', '゚');
        return text.Normalize(NormalizationForm.FormKC);
    }
}
