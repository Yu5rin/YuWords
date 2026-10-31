namespace YuWords.Core;

/// <summary>読み込んだメールから候補一覧を作るまでの一連の処理</summary>
public static class Extraction
{
    /// <summary>
    /// 形態素解析・集計・除外リストの適用・IME 変換結果の取得をまとめて行う。
    /// IME への問い合わせは専用の STA スレッドで行う
    /// </summary>
    static readonly System.Text.RegularExpressions.Regex Hira = new(@"^[ぁ-んー]+$");

    /// <summary>
    /// IME が返した表記のよみを候補に付ける。形態素解析でよみが作れなかった語は、IME のよみを推測として使う（要確認のまま）。
    /// 推測と IME のよみが食い違う語は、どちらが正しいか確かめてもらうため要確認にする（連絡先のふりがなは除く）
    /// </summary>
    public static void ApplyImeReading(Candidate c, string imeReading)
    {
        // 定型文のよみは短縮よみ（自分で決める）なので、IME の文のよみは使わない
        if (c.Category == CandidateBuilder.PhraseCategory || !Hira.IsMatch(imeReading)) return;   // 英字が全角のまま残ったものなどは使わない
        c.ImeReading = imeReading;
        if (c.Reading.Length == 0)
        {
            c.Reading = imeReading;
            c.ReadingSource = "IME";
        }
        else if (c.Reading != imeReading && c.ReadingSource is "" or "IME")
            c.Unsure = true;   // 連絡先・前回の修正で決まったよみはそのまま
    }

    /// <summary>前回までに手で直したよみを、同じ語に使う（要確認を外す）。戻り値は使った数</summary>
    public static int ApplyLearned(List<Candidate> rows, IReadOnlyDictionary<string, string> learned)
    {
        int n = 0;
        foreach (var c in rows)
        {
            if (!learned.TryGetValue(c.Word, out var r) || r.Length == 0) continue;
            c.Reading = r;
            c.Unsure = false;
            c.ReadingSource = "前回の修正";
            n++;
        }
        return n;
    }

    public static List<Candidate> Run(IReadOnlyList<List<string>> mails, ExtractorSettings settings,
        IReadOnlySet<string>? excluded = null, IProgress<string>? progress = null, CancellationToken ct = default,
        Func<IReadOnlyCollection<string>, CancellationToken, NameReadings.Map>? nameLookup = null,
        IReadOnlyDictionary<string, string>? learned = null)
    {
        progress?.Report("形態素解析中...");
        List<Candidate> rows;
        using (var tok = new MorphTokenizer())
        {
            var ex = new TermExtractor(tok, settings);
            rows = CandidateBuilder.Build(mails, ex, settings, progress, ct);
        }
        if (excluded != null)
            rows.RemoveAll(c => excluded.Contains(c.Word));
        if (nameLookup != null && rows.Count > 0)
        {
            progress?.Report("人名のふりがなを探しています...");
            if (NameReadings.Apply(rows, nameLookup(NameReadings.Targets(rows), ct)) is > 0 and var n)
                progress?.Report($"登録されたふりがなで {n} 語のよみを補いました");
        }

        if (learned != null) ApplyLearned(rows, learned);

        progress?.Report("IME の変換結果を取得中...");
        if (ImeLookup.Fill(rows, ct) is { } error)
            progress?.Report($"[警告] IME 変換エンジンに接続できませんでした（{error}）。変換結果は空になります。");
        return rows;
    }
}
