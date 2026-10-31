namespace YuWords.Core;

/// <summary>IME への直接登録の結果</summary>
public sealed class RegistrationReport
{
    public int Registered { get; set; }
    public int Skipped { get; set; }
    public List<(string Reading, string Word, string Pos)> Failed { get; } = [];
    /// <summary>登録できた語と既にあった語（登録済みリストに入れるもの）</summary>
    public List<(string Reading, string Word, string Pos)> Succeeded { get; } = [];
}

/// <summary>語の一覧を IME のユーザー辞書に登録し、登録済みリストと持ち出し用辞書を更新する</summary>
public static class ImeRegistration
{
    /// <summary>
    /// IME の COM を使うので STA スレッドで呼ぶこと。
    /// ユーザー辞書を開けなければ InvalidOperationException（何も登録しない）
    /// </summary>
    /// <summary>IME のユーザー辞書を直接使ってよいか（設定。前回落ちたら false になる）</summary>
    public static bool Enabled { get; set; } = true;

    public static RegistrationReport Run(IReadOnlyList<(string Reading, string Word, string Pos)> words, AppStore store,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (!Enabled)
            throw new InvalidOperationException("IME のユーザー辞書を直接使う設定が OFF です（［設定］→［抽出］）。");
        var report = new RegistrationReport();
        using (var dic = ImeDictionary.Open())
        {
            for (int i = 0; i < words.Count; i++)
            {
                if (ct.IsCancellationRequested) break;   // 途中までの結果は記録する
                var w = words[i];
                if (w.Reading.Length == 0) { report.Failed.Add(w); continue; }
                switch (dic.Register(w.Reading, w.Word, w.Pos))
                {
                    case RegisterResult.Registered: report.Registered++; report.Succeeded.Add(w); break;
                    case RegisterResult.Exists: report.Skipped++; report.Succeeded.Add(w); break;
                    default: report.Failed.Add(w); break;
                }
                if ((i + 1) % 20 == 0) progress?.Report(i + 1);
            }
        }
        store.AddRegistered(report.Succeeded.Select(w => (w.Word, w.Reading, w.Pos)));
        ImeLookup.ForgetConversions(report.Succeeded.Select(w => w.Reading));   // 登録で変換結果が変わるため
        return report;
    }

    /// <summary>IME のユーザー辞書の語（よみ・表記）。開けなければ null</summary>
    public static HashSet<(string Word, string Reading)>? ReadUserDictionary()
    {
        if (!Enabled) return null;
        try
        {
            using var dic = ImeDictionary.Open();
            return dic.GetUserWords().Select(w => (w.Word, w.Reading)).ToHashSet();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>IME のユーザー辞書の語をすべて登録済みリストに入れる（持ち出し用辞書に含めるため）。戻り値は追加した数</summary>
    public static int ImportUserDictionary(AppStore store)
    {
        if (!Enabled) throw new InvalidOperationException("IME のユーザー辞書を直接使う設定が OFF です（［設定］→［抽出］）。");
        using var dic = ImeDictionary.Open();
        return store.AddRegistered(dic.GetUserWords().Select(w => (w.Word, w.Reading, w.Pos)));
    }

    /// <summary>登録済みの語を IME のユーザー辞書から消し、登録済みリストからも外す。戻り値は IME から消せた数</summary>
    public static int Unregister(IReadOnlyList<RegisteredWord> words, AppStore store)
    {
        if (!Enabled) throw new InvalidOperationException("IME のユーザー辞書を直接使う設定が OFF です（［設定］→［抽出］）。");
        int n = 0;
        // Google 日本語入力に書き出した語は、Google の辞書ツールで消してもらう（ここでは一覧から外すだけ）
        var msime = words.Where(w => w.Target == "MSIME").ToList();
        if (msime.Count > 0)
            using (var dic = ImeDictionary.Open())
                foreach (var w in msime)
                    if (dic.Delete(w.Reading, w.Word, w.Pos)) n++;
        store.RemoveRegistered(words);
        ImeLookup.ForgetConversions(words.Select(w => w.Reading));
        return n;
    }
}
