using System.Diagnostics;
using YuWords.Core;

namespace YuWords;

// 確認画面の読み込み（取り込み元・抽出・追加読み込み）
sealed partial class MainForm
{
    // ===== 読み込み =====

    /// <summary>取り込み元。Load は STA スレッドで呼ばれ、文書ごとの行リストを返す</summary>
    /// <summary>読み込み済みの取り込み元（追加読み込みで数え直すために文書を持っておく）</summary>
    sealed record LoadedSource(string Name, string Unit, List<List<string>> Docs);

    List<LoadedSource> loaded = [];

    /// <param name="NewOnly">前回以降の差分。登録済みの語は一覧に出さない</param>
    /// <param name="OnLoaded">読み込みが終わったときの処理（前回の日時の記録など）</param>
    /// <param name="TracksUsage">登録済みの語がまだ使われているかを、この文章で調べるか（ふだん書く文章＝Outlook の送信メールだけ）</param>
    sealed record Source(string Name, string Unit, Func<IProgress<int>, CancellationToken, List<List<string>>> Load,
        bool NewOnly = false, Action? OnLoaded = null, bool TracksUsage = false);

    Source OutlookSource(bool newOnly)
    {
        var s = store.Settings.Extractor;
        var since = newOnly ? store.Settings.LastOutlookRead : null;
        var started = DateTime.Now;
        return new(since is { } t ? $"Outlook の新しいメール（{t:M/d H:mm} 以降）" : "Outlook の送信済みメール", "メール",
            (p, ct) => OutlookMailSource.FetchSentMails(s.Days, s.MaxMails, p, ct, since),
            NewOnly: since != null,
            TracksUsage: true,
            OnLoaded: () =>
            {
                store.Settings.LastOutlookRead = started;
                store.SaveSettings();
            });
    }

    /// <summary>
    /// 人名のよみを、Outlook の連絡先と会社のアドレス帳のふりがなで補う処理（設定で切り替え）。
    /// 取り込み元がメールでなくても、名前は同じ人のことが多いので使う
    /// </summary>
    Func<IReadOnlyCollection<string>, CancellationToken, NameReadings.Map>? NameLookup()
    {
        bool contacts = store.Settings.UseContactReadings, gal = store.Settings.UseGalReadings;
        if (!contacts && !gal) return null;
        return (targets, ct) =>
        {
            var map = contacts ? NameReadings.FromOutlookContacts(ct) : new NameReadings.Map();
            if (gal) map.AddRange(NameReadings.FromGlobalAddressList(targets, ct));
            return map;
        };
    }

    public const string NewOnlyArgument = "--new";

    /// <summary>起動したら、前回以降の新しいメールだけを読み込む（自動チェックの通知から開いたとき）</summary>
    public void LoadNewOnShown()
    {
        Shown += async (_, _) =>
        {
            if (store.Settings.LastOutlookRead == null) return;
            if (all.Count > 0) btnAppend.Checked = true;   // 途中の一覧があれば、それに足す
            await LoadAsync(OutlookSource(newOnly: true));
        };
    }

    /// <summary>「今の一覧に追加」を切り替える（画面確認用）</summary>
    public void SetAppend(bool on) => btnAppend.Checked = on;

    /// <summary>メールのテキストを読み込む（画面確認用の --screenshot から使う）</summary>
    public Task LoadTextAsync(string path) =>
        LoadAsync(new Source(Path.GetFileName(path), "メール", (_, _) => MailTextSource.Read(path)));

    async Task LoadMailTextAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "メールのテキストを開く（「---」だけの行でメールを区切る）",
            Filter = "テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        await LoadTextAsync(dlg.FileName);
    }

    async Task LoadFolderAsync()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "取り込むフォルダ（Obsidian の Vault など）。中の .md・.txt・.docx・.pptx を1ファイル1文書として読みます",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(store.Settings.LastFolder) ? store.Settings.LastFolder : "",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        store.Settings.LastFolder = dlg.SelectedPath;
        store.SaveSettings();
        await LoadFolderAsync(dlg.SelectedPath);
    }

    /// <summary>フォルダを読み込む（画面確認用の --screenshot からも使う）</summary>
    public Task LoadFolderAsync(string folder) =>
        LoadAsync(new Source(Path.GetFileName(folder), "文書", (p, ct) => DocumentSources.ReadFolder(folder, null, p, ct,
            store.Settings.FolderDays > 0 ? DateTime.Now.AddDays(-store.Settings.FolderDays) : null)));

    async Task LoadTableAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "取り込む表（Excel・CSV）",
            Filter = "Excel・CSV (*.xlsx;*.xlsm;*.csv;*.tsv)|*.xlsx;*.xlsm;*.csv;*.tsv|すべてのファイル (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        List<DocumentSources.Sheet> sheets;
        try
        {
            UseWaitCursor = true;
            sheets = DocumentSources.ReadTable(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"読み込めませんでした。\n{ex.Message}", "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally { UseWaitCursor = false; }

        using var pick = new TableColumnForm(Path.GetFileName(dlg.FileName), sheets);
        if (pick.ShowDialog(this) != DialogResult.OK) return;
        if (pick.Glossary)
        {
            // 用語集: 1行に1語なので出現数で絞らず、語句とよみの組をそのまま候補にする
            var words = pick.Entries().Select(e => new DictWord(e.Reading, e.Word, "名詞", e.Note, e.Adjusted, e.Extra)).ToList();
            await AddWordsAsync(Path.GetFileName(dlg.FileName), words, "用語集");
            return;
        }
        var docs = pick.Documents();
        await LoadAsync(new Source($"{Path.GetFileName(dlg.FileName)} の {pick.ColumnLabel}", "行", (_, _) => docs));
    }

    /// <summary>
    /// 数え直し・追加読み込みのあと、抽出では出てこない行（手で追加した語・共有辞書の語）を前の一覧から戻す
    /// </summary>
    void KeepAddedItems(Dictionary<string, ReviewItem> old)
    {
        var fresh = all.Select(i => i.Source.Word).ToHashSet();
        all.AddRange(old.Values.Where(o => o.Category is "手入力" or "共有辞書" or "用語集" or "チーム辞書" && !fresh.Contains(o.Source.Word)));
    }

    /// <summary>候補から一覧の行を作る。前の一覧での選択・保留にしたときの選択を引き継ぐ</summary>
    List<ReviewItem> BuildItems(List<Candidate> rows, HashSet<(string Word, string Reading)>? imeWords,
        Dictionary<string, ReviewItem> old) =>
        rows.Select(c =>
        {
            bool inIme = imeWords?.Contains((c.Word, c.Reading)) == true;
            var item = new ReviewItem(c, inIme || store.IsRegistered(c.Word, c.Reading)) { InImeDictionary = inIme };
            if (old.TryGetValue(c.Word, out var o)) item.CopyChoicesFrom(o);
            else if (store.Held.TryGetValue(c.Word, out var h))
            {
                item.Restore(h.State);   // 保留にしたときのよみ・品詞のまま
                item.InImeDictionary = inIme;
                item.IsRegistered = item.ComputeRegistered(store.IsRegistered);
            }
            return item;
        }).ToList();

    /// <summary>
    /// 読み込み済みの文書を、今の設定（出現数・ストップワード・よみのルールなど）で数え直す。
    /// 読み込み直さないので速い。選択は引き継ぐ
    /// </summary>
    async Task RecountAsync()
    {
        if (cts != null) return;
        var docs = loaded.SelectMany(l => l.Docs).ToList();
        if (docs.Count == 0)
        {
            SetStatus(all.Count > 0 ? "前回の続きから再開した一覧は、元の文章を持っていないので数え直せません。読み込み直してください。"
                                    : "数え直す一覧がありません。");
            return;
        }
        cts = new CancellationTokenSource();
        SetBusy(true);
        var settings = store.Settings.Extractor;
        var excluded = new HashSet<string>(store.Excluded);
        var progress = new Progress<string>(SetStatus);
        var nameLookup = NameLookup();
        var learned = new Dictionary<string, string>(store.Learned);
        bool checkIme = store.Settings.CheckImeDictionary;
        var sw = Stopwatch.StartNew();
        try
        {
            var token = cts.Token;
            var (rows, imeWords) = await StaTask.Run(() =>
            {
                var r = Extraction.Run(docs, settings, excluded, progress, token, nameLookup, learned);
                return (r, checkIme && r.Count > 0 ? ImeRegistration.ReadUserDictionary(token) : null);
            });
            var old = all.GroupBy(i => i.Source.Word).ToDictionary(g => g.Key, g => g.First());
            int before = all.Count;
            all = BuildItems(rows, imeWords, old);
            KeepAddedItems(old);
            undo.Clear();
            Refilter();
            SaveSession();
            Log.Info($"数え直し: {docs.Count} 件 → 候補 {all.Count} 語（{sw.Elapsed.TotalSeconds:F1} 秒）");
            SetStatus($"今の設定で数え直しました。候補 {before} 語 → {all.Count} 語（{sw.Elapsed.TotalSeconds:F1} 秒）。選択は引き継いでいます。");
        }
        catch (OperationCanceledException)
        {
            SetStatus("中止しました。");
        }
        catch (Exception ex)
        {
            Log.Error("数え直しに失敗", ex);
            SetStatus("数え直しに失敗しました。");
            MessageBox.Show(this, ex.Message, "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            cts.Dispose();
            cts = null;
            SetBusy(false);
        }
    }

    /// <summary>
    /// 同僚から受け取った辞書（Microsoft IME・Google 日本語入力・ATOK のテキストか CSV）を、今の一覧に候補として足す。
    /// よみは辞書のものを使う。一覧に同じ語があれば足さない
    /// </summary>
    async Task LoadShared()
    {
        if (cts != null) return;
        using var dlg = new OpenFileDialog
        {
            Title = "同僚の辞書を開く（Microsoft IME・Google 日本語入力・ATOK のテキスト、CSV）",
            Filter = "辞書のテキスト・CSV (*.txt;*.csv)|*.txt;*.csv|すべてのファイル (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        List<(string Reading, string Word, string Pos)> words;
        try { words = DictionaryFormats.Read(dlg.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"読み込めませんでした。\n{ex.Message}", "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        await AddWordsAsync(Path.GetFileName(dlg.FileName), words.Select(w => new DictWord(w.Reading, w.Word, w.Pos)).ToList(), "共有辞書");
    }

    /// <summary>
    /// 辞書・用語集の語（よみ付き）を、今の一覧に候補として足す。一覧に同じ語があれば足さない。
    /// よみの無い語は IME のよみを推測として使い、要確認にする。IME への問い合わせは裏で並べて行う
    /// </summary>
    /// <summary>辞書・用語集の1語（意味は例文の欄に出す。Adjusted は語句を自動で直したので要確認にする）</summary>
    sealed record DictWord(string Reading, string Word, string Pos, string Note = "", bool Adjusted = false, string Extra = "");

    // ===== チームの辞書 =====

    /// <summary>起動したときに、チームの辞書にまだ知らせていない新しい語があれば知らせる</summary>
    void CheckTeamDictionaryOnStartup()
    {
        var s = store.Settings;
        if (!s.TeamNotify || s.TeamDictionaryPath.Length == 0) return;
        Shown += async (_, _) =>
        {
            await Task.Delay(800);
            await CheckTeamDictionaryAsync(manual: false);
        };
    }

    /// <summary>チームの辞書を読み、新しい語を一覧に足すか尋ねる。manual なら、新しい語が無くても知らせる</summary>
    async Task CheckTeamDictionaryAsync(bool manual)
    {
        if (cts != null) return;
        var path = store.Settings.TeamDictionaryPath;
        if (path.Length == 0)
        {
            if (manual) MessageBox.Show(this, "チームの辞書の場所が決まっていません。［設定］→［チーム］で、社内の共有フォルダの CSV を選んでください。", "チームの辞書");
            return;
        }
        List<TeamWord> team;
        try { team = await Task.Run(() => TeamDictionary.Read(path)); }
        catch (Exception ex)
        {
            Log.Error("チームの辞書を読めませんでした", ex);
            if (manual) MessageBox.Show(this, $"チームの辞書を読めませんでした。\n{ex.Message}", "チームの辞書", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var inList = all.Select(i => i.Source.Word).ToHashSet();
        var fresh = TeamDictionary.NewWords(team, store).Where(w => !inList.Contains(w.Word)).ToList();
        if (fresh.Count == 0)
        {
            if (manual) MessageBox.Show(this, $"チームの辞書（{team.Count} 語）に、新しい語はありません。", "チームの辞書");
            return;
        }
        var sample = string.Join("、", fresh.Take(8).Select(w => w.Word)) + (fresh.Count > 8 ? " など" : "");
        var by = fresh.Select(w => w.AddedBy).Where(b => b.Length > 0).Distinct().Take(3).ToList();
        var ans = MessageBox.Show(this,
            $"チームの辞書に新しい語が {fresh.Count} 語あります" + (by.Count > 0 ? $"（{string.Join("・", by)} さんなどが追加）" : "") + $"。\n{sample}\n\n" +
            "一覧に足しますか？\n［はい］一覧に足す　［いいえ］今回は足さない（次からは知らせない）　［キャンセル］あとで",
            "チームの辞書", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
        if (ans == DialogResult.Cancel) return;
        if (ans == DialogResult.Yes)
        {
            await AddWordsAsync("チームの辞書", fresh.Select(w => new DictWord(w.Reading, w.Word, w.Pos,
                w.AddedBy.Length > 0 ? $"{w.AddedBy} さんが {w.AddedAt} に追加" : "")).ToList(), "チーム辞書");
            // 一覧に入った語だけを知らせ済みにする（ほかの読み込み中・中止で入らなかった語は、次にまた知らせる）
            var nowInList = all.Select(i => i.Source.Word).ToHashSet();
            fresh = fresh.Where(w => nowInList.Contains(w.Word)).ToList();
        }
        store.MarkTeamSeen(fresh.Select(w => TeamDictionary.Key(w.Word, w.Reading)));
    }

    /// <summary>選んだ行（よみのある語）をチームの辞書に足す</summary>
    async Task AddToTeamAsync(IEnumerable<(string Word, string Reading, string Pos)> words)
    {
        var path = store.Settings.TeamDictionaryPath;
        if (path.Length == 0)
        {
            MessageBox.Show(this, "チームの辞書の場所が決まっていません。［設定］→［チーム］で、社内の共有フォルダの CSV を選んでください。", "チームの辞書");
            return;
        }
        var list = words.Where(w => w.Reading.Length > 0).ToList();
        if (list.Count == 0) return;
        try
        {
            int n = await Task.Run(() => TeamDictionary.Add(path, list, Environment.UserName));
            // 自分で足した語は、次の起動で知らせない
            store.MarkTeamSeen(list.Select(w => TeamDictionary.Key(w.Word, w.Reading)));
            Log.Info($"チームの辞書に {n} 語を追加");
            SetStatus(n > 0 ? $"チームの辞書に {n} 語を足しました（{list.Count - n} 語はすでにありました）。"
                            : "選んだ語は、すべてチームの辞書にありました。");
        }
        catch (Exception ex)
        {
            Log.Error("チームの辞書に足せませんでした", ex);
            MessageBox.Show(this, $"チームの辞書に足せませんでした。\n{ex.Message}", "チームの辞書", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    async Task AddWordsAsync(string name, List<DictWord> words, string category)
    {
        if (cts != null) return;
        var have = all.Select(i => i.Source.Word).ToHashSet();
        var pos = new Dictionary<string, string>();
        var candidates = new List<Candidate>();
        foreach (var w in words.DistinctBy(w => w.Word))
        {
            if (have.Contains(w.Word) || store.Excluded.Contains(w.Word)) continue;
            pos[w.Word] = w.Pos;
            var from = $"{name} から" + (w.Adjusted ? "（語句を英字だけにしました。確かめてください）" : "");
            candidates.Add(new Candidate
            {
                Word = w.Word, Reading = w.Reading, Category = category, Unsure = w.Reading.Length == 0 || w.Adjusted,
                ReadingSource = w.Reading.Length > 0 ? category : "",
                Example = w.Note.Length > 0 ? w.Note : from,
                Examples = w.Note.Length > 0 ? [w.Note, from] : [from],
                ExtraReadings = w.Extra.Length > 0 ? [w.Extra] : [],
            });
        }
        if (candidates.Count == 0)
        {
            SetStatus($"{name} に、今の一覧に無い語はありませんでした。");
            return;
        }

        cts = new CancellationTokenSource();
        SetBusy(true);
        SetStatus($"{name} の {candidates.Count} 語を IME に問い合わせています...");
        bool checkIme = store.Settings.CheckImeDictionary;
        HashSet<(string Word, string Reading)>? imeWords;
        try
        {
            var token = cts.Token;
            imeWords = await StaTask.Run(() =>
            {
                ImeLookup.Fill(candidates, token);
                return checkIme ? ImeRegistration.ReadUserDictionary(token) : null;
            });
        }
        catch (OperationCanceledException)
        {
            SetStatus("中止しました。");
            return;
        }
        catch (Exception ex)
        {
            Log.Error($"{category}の取り込みに失敗", ex);
            MessageBox.Show(this, ex.Message, "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            cts.Dispose();
            cts = null;
            SetBusy(false);
        }

        var added = candidates.Select(c =>
        {
            bool inIme = imeWords?.Contains((c.Word, c.Reading)) == true;
            var item = new ReviewItem(c, inIme || store.IsRegistered(c.Word, c.Reading)) { InImeDictionary = inIme, Pos = pos[c.Word] };
            item.Register = !item.IsRegistered && !c.Unsure && c.Reading.Length > 0;
            return item;
        }).ToList();
        all.AddRange(added);
        PushUndo($"{category}（{name}）を追加", () => all.RemoveAll(added.Contains));
        restoredSources = [.. (loaded.Count > 0 ? loaded.Select(l => l.Name) : restoredSources), name];
        if (loaded.Count > 0) loaded = [.. loaded, new LoadedSource(name, "", [])];
        Text = $"YuWords - {string.Join(" ＋ ", loaded.Count > 0 ? loaded.Select(l => l.Name) : restoredSources)}";
        Refilter();
        ScheduleSave();
        int noReading = added.Count(i => i.Source.ReadingSource == "IME");
        int registered = added.Count(i => i.IsRegistered);
        Log.Info($"{category}: {words.Count} 語中 {added.Count} 語を追加（よみなし {noReading}、登録済み {registered}）");
        SetStatus($"{name} から {added.Count} 語を一覧に足しました（分類「{category}」）。" +
                  (noReading > 0 ? $"よみの無かった {noReading} 語は IME のよみを入れ、要確認にしています。" : "") +
                  (registered > 0 ? $"登録済みの {registered} 語は灰色で出ます。" : "") +
                  "よみを確かめて「IME に登録」してください。");
    }

    async Task LoadAsync(Source source)
    {
        bool merge = btnAppend.Checked && all.Count > 0;
        // 途中保存から再開した一覧は元の文書を持っていないので、数え直さずに新しい候補を足す
        bool keepOld = merge && loaded.Count == 0;
        if (!merge && all.Any(i => i.Register && !i.IsRegistered) &&
            MessageBox.Show(this, "いまの一覧の登録チェックは、まだ登録も出力もしていません。読み込み直しますか？\n" +
                                  "（「今の一覧に追加」を ON にすると、選択を残したまま別の取り込み元を足せます）", "YuWords",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        cts = new CancellationTokenSource();
        SetBusy(true);
        var settings = store.Settings.Extractor;
        var excluded = new HashSet<string>(store.Excluded);
        var previous = merge ? loaded.ToList() : [];
        // Progress は作ったスレッドの同期コンテキストで通知するので、UI スレッドで作っておく
        var progress = new Progress<string>(SetStatus);
        var docProgress = new Progress<int>(n => SetStatus($"{source.Name} を読み込み中... {n} 件"));
        var sw = Stopwatch.StartNew();
        SetStatus($"{source.Name} を読み込み中...");
        try
        {
            var token = cts.Token;
            bool checkIme = store.Settings.CheckImeDictionary;
            var nameLookup = NameLookup();
            var learned = new Dictionary<string, string>(store.Learned);
            var registeredWords = store.Registered.Select(r => r.Word).ToList();
            HashSet<string>? used = null;
            var (docs, rows, imeWords) = await StaTask.Run(() =>
            {
                var d = source.Load(docProgress, token);
                // 登録済みの語がまだ文章に出てくるか（使わなくなった語を見つけるため）
                if (source.TracksUsage && d.Count > 0 && registeredWords.Count > 0) used = WordUsage.Find(d, registeredWords, token);
                // 追加のときは、前の取り込み元と合わせて数え直す（同じ語が両方に出れば合算される）
                var combined = previous.Select(p => p.Docs).Append(d).SelectMany(x => x).ToList();   // 再開した一覧の分は空
                var r = d.Count == 0 ? [] : Extraction.Run(combined, settings, excluded, progress, token, nameLookup, learned);
                var ime = checkIme && r.Count > 0 ? ImeRegistration.ReadUserDictionary(token) : null;
                return (d, r, ime);
            });

            if (docs.Count == 0)
            {
                SetStatus($"{source.Name} に読み込める{source.Unit}がありませんでした。");
                return;
            }

            // 追加のときは、前の一覧での選択（登録・語句・よみ・品詞）を引き継ぐ
            var old = merge ? all.GroupBy(i => i.Source.Word).ToDictionary(g => g.Key, g => g.First()) : [];
            all = BuildItems(rows, imeWords, old);
            KeepAddedItems(old);
            if (keepOld)
            {
                var fresh = all.Select(i => i.Source.Word).ToHashSet();
                all.AddRange(old.Values.Where(o => !fresh.Contains(o.Source.Word)));
            }
            if (source.NewOnly) all.RemoveAll(i => i.IsRegistered);   // 差分では、もう登録した語は出さない
            source.OnLoaded?.Invoke();
            if (used != null) store.MarkUsed(used, DateTime.Now);
            loaded = [.. previous, new LoadedSource(source.Name, source.Unit, docs)];
            if (keepOld) loaded = [.. restoredSources.Select(n => new LoadedSource(n, "", [])), .. loaded];
            restoredSources = [];
            undo.Clear();

            // 読み込み直後は、登録する価値の高い順（おすすめ順）に並べる
            (sortCol, sortDesc) = (Col.Stars, true);
            foreach (DataGridViewColumn c in grid.Columns) c.HeaderCell.SortGlyphDirection = SortOrder.None;
            grid.Columns[(int)Col.Stars].HeaderCell.SortGlyphDirection = SortOrder.Descending;
            var units = loaded.Select(l => l.Unit).Where(u => u.Length > 0).Distinct().ToList();
            bool mail = units is ["メール"];
            grid.Columns[(int)Col.MailCount].HeaderText = units.Count > 1 ? "出現数" : mail ? "出現メール数" : $"出現{units[0]}数";
            // メール以外では「署名」ではなく、多くの文書に繰り返し出る定型の行
            grid.Columns[(int)Col.Signature].HeaderText = mail ? "署名" : units.Contains("メール") ? "署名・定型" : "定型";
            chkSig.Text = mail ? "署名由来のみ" : "定型行由来のみ";
            Text = $"YuWords - {string.Join(" ＋ ", loaded.Select(l => l.Name))}";
            Refilter();
            var what = loaded.Count > 1
                ? string.Join("、", loaded.Select(l => $"{l.Name} {l.Docs.Count} 件"))
                : $"{docs.Count} 件の{source.Unit}";
            SaveSession();
            if (rows.Count == 0 && !merge)
            {
                // 文書はあるのに候補が無い: 出現数の条件で全部落ちたことが多い（用語集など1件ずつの表）
                MessageBox.Show(this,
                    $"{docs.Count} 件の{source.Unit}を読みましたが、候補になる語はありませんでした。\n\n" +
                    $"候補にするのは {settings.MinMails} 件以上に出た語だけです（［設定］→［抽出］の「候補にする出現数」）。\n" +
                    "1行に1語の用語集なら、「ほかから読み込む」→「Excel・CSV」で「用語集として取り込む」を選んでください。",
                    "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            Log.Info($"読み込み: {source.Name} {docs.Count} 件 → 候補 {all.Count} 語（{sw.Elapsed.TotalSeconds:F1} 秒、追加={merge}）");
            SetStatus($"{what}から {all.Count} 語の候補を抽出しました（{sw.Elapsed.TotalSeconds:F1} 秒）。" +
                      "黄色のよみは推測です。確認して直すと登録チェックが付きます。");
        }
        catch (OperationCanceledException)
        {
            Log.Info($"読み込みを中止: {source.Name}");
            SetStatus("中止しました。");
        }
        catch (Exception ex)
        {
            Log.Error($"読み込みに失敗: {source.Name}", ex);
            SetStatus("読み込みに失敗しました。");
            MessageBox.Show(this, ex.Message, "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            cts.Dispose();
            cts = null;
            SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        btnOutlook.Enabled = btnOther.Enabled = btnSettings.Enabled = btnDictTool.Enabled = !busy;
        btnClear.Enabled = btnBulk.Enabled = btnAppend.Enabled = btnAddWord.Enabled = !busy;
        btnCancel.Enabled = busy;
        progressBar.Visible = busy;
        grid.Enabled = !busy;
        UseWaitCursor = busy;
        UpdateCounts();
    }
}
