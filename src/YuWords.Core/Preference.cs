using System.Text.RegularExpressions;

namespace YuWords.Core;

/// <summary>
/// 利用者の好みに合わせたおすすめ度の補正。これまでに登録した語と除外した語を、
/// 字の種類（漢字だけ・カタカナだけ・英字を含む など）と長さで分け、登録した割合が高い種類の語ほど上に出す。
/// 語そのものは覚えない（種類ごとの数だけ）
/// </summary>
public sealed class Preference
{
    static readonly Regex KanjiOnly = new(@"^[\p{IsCJKUnifiedIdeographs}々〆ヶ]+$");
    static readonly Regex KataOnly = new(@"^[ァ-ヶー・]+$");
    static readonly Regex HasAscii = new(@"[A-Za-z]");
    static readonly Regex HasKanji = new(@"[\p{IsCJKUnifiedIdeographs}々]");
    static readonly Regex HasKata = new(@"[ァ-ヶ]");

    /// <summary>これより少ない数しか覚えていなければ補正しない（数語の傾向で順番を動かさない）</summary>
    public const int MinSamples = 30;
    /// <summary>種類ごとに、これより少なければその種類は補正しない</summary>
    const int MinPerFeature = 5;
    const double MinFactor = 0.6, MaxFactor = 1.6;

    readonly Dictionary<string, double> factors;

    Preference(Dictionary<string, double> factors, int samples) => (this.factors, Samples) = (factors, samples);

    /// <summary>補正なし</summary>
    public static Preference None { get; } = new([], 0);

    /// <summary>学んだ語の数（登録した語＋除外した語）</summary>
    public int Samples { get; }

    /// <summary>語の種類（字の種類と長さ）</summary>
    public static string Feature(string word)
    {
        string script =
            HasAscii.IsMatch(word) ? (HasKanji.IsMatch(word) || HasKata.IsMatch(word) ? "英字＋日本語" : "英字") :
            KanjiOnly.IsMatch(word) ? "漢字" :
            KataOnly.IsMatch(word) ? "カタカナ" :
            HasKanji.IsMatch(word) && HasKata.IsMatch(word) ? "漢字＋カタカナ" : "その他";
        string length = word.Length switch { <= 2 => "2字まで", <= 4 => "3〜4字", <= 7 => "5〜7字", _ => "8字以上" };
        return $"{script}・{length}";
    }

    /// <summary>登録した語と除外した語から学ぶ</summary>
    public static Preference Learn(IEnumerable<string> registered, IEnumerable<string> excluded)
    {
        var reg = registered.Distinct().ToList();
        var exc = excluded.Distinct().Except(reg).ToList();
        int total = reg.Count + exc.Count;
        if (total < MinSamples || reg.Count == 0 || exc.Count == 0) return new([], total);
        double baseRate = (double)reg.Count / total;
        var counts = new Dictionary<string, (int Reg, int Exc)>();
        foreach (var w in reg) { var f = Feature(w); counts[f] = (counts.GetValueOrDefault(f).Reg + 1, counts.GetValueOrDefault(f).Exc); }
        foreach (var w in exc) { var f = Feature(w); counts[f] = (counts.GetValueOrDefault(f).Reg, counts.GetValueOrDefault(f).Exc + 1); }
        var factors = new Dictionary<string, double>();
        foreach (var (f, (r, e)) in counts)
        {
            if (r + e < MinPerFeature) continue;
            // 全体の割合に寄せてならす（数が少ない種類で大きく振れないように）
            const double prior = 4;
            double rate = (r + prior * baseRate) / (r + e + prior);
            factors[f] = Math.Clamp(rate / baseRate, MinFactor, MaxFactor);
        }
        return new(factors, total);
    }

    /// <summary>おすすめ度に掛ける数（1 なら補正なし）</summary>
    public double Factor(string word) => factors.TryGetValue(Feature(word), out var f) ? f : 1.0;
}
