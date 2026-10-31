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
    sealed record Source(string Name, string Unit, Func<IProgress<int>, CancellationToken, List<List<string>>> Load,
        bool NewOnly = false, Action? OnLoaded = null);

    Source OutlookSource(bool newOnly)
    {
        var s = store.Settings.Extractor;
        var since = newOnly ? store.Settings.LastOutlookRead : null;
        var started = DateTime.Now;
        return new(since is { } t ? $"Outlook の新しいメール（{t:M/d H:mm} 以降）" : "Outlook の送信済みメール", "メール",
            (p, ct) => OutlookMailSource.FetchSentMails(s.Days, s.MaxMails, p, ct, since),
            NewOnly: since != null,
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
        var docs = DocumentSources.ColumnDocuments(pick.Sheet, pick.Column, pick.SkipHeader);
        await LoadAsync(new Source($"{Path.GetFileName(dlg.FileName)} の {pick.ColumnLabel}", "行", (_, _) => docs));
    }

    /// <summary>
    /// 数え直し・追加読み込みのあと、抽出では出てこない行（手で追加した語・共有辞書の語）を前の一覧から戻す
    /// </summary>
    void KeepAddedItems(Dictionary<string, ReviewItem> old)
    {
        var fresh = all.Select(i => i.Source.Word).ToHashSet();
        all.AddRange(old.Values.Where(o => o.Category is "手入力" or "共有辞書" && !fresh.Contains(o.Source.Word)));
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
                return (r, checkIme && r.Count > 0 ? ImeRegistration.ReadUserDictionary() : null);
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
    void LoadShared()
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
        var have = all.Select(i => i.Source.Word).ToHashSet();
        var name = Path.GetFileName(dlg.FileName);
        var added = new List<ReviewItem>();
        foreach (var (reading, word, pos) in words.DistinctBy(w => w.Word))
        {
            if (have.Contains(word) || store.Excluded.Contains(word)) continue;
            var c = new Candidate
            {
                Word = word, Reading = reading, Category = "共有辞書", ReadingSource = "共有辞書",
                Example = $"{name} から", ImeReading = UiIme.Phonetic(word),
            };
            var item = new ReviewItem(c, store.IsRegistered(word, reading)) { Pos = pos };
            item.Register = !item.IsRegistered;
            Reconvert(item);
            added.Add(item);
        }
        if (added.Count == 0)
        {
            SetStatus($"{name} に、今の一覧に無い語はありませんでした。");
            return;
        }
        all.AddRange(added);
        PushUndo($"共有辞書（{name}）を追加", () => all.RemoveAll(added.Contains));
        restoredSources = [.. (loaded.Count > 0 ? loaded.Select(l => l.Name) : restoredSources), name];
        if (loaded.Count > 0) loaded = [.. loaded, new LoadedSource(name, "", [])];
        Text = $"YuWords - {string.Join(" ＋ ", loaded.Count > 0 ? loaded.Select(l => l.Name) : restoredSources)}";
        Refilter();
        ScheduleSave();
        Log.Info($"共有辞書: {words.Count} 語中 {added.Count} 語を追加");
        SetStatus($"{name} から {added.Count} 語を一覧に足しました（分類「共有辞書」）。登録済みの語は灰色で出ます。");
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
            var (docs, rows, imeWords) = await StaTask.Run(() =>
            {
                var d = source.Load(docProgress, token);
                // 追加のときは、前の取り込み元と合わせて数え直す（同じ語が両方に出れば合算される）
                var combined = previous.Select(p => p.Docs).Append(d).SelectMany(x => x).ToList();   // 再開した一覧の分は空
                var r = d.Count == 0 ? [] : Extraction.Run(combined, settings, excluded, progress, token, nameLookup, learned);
                var ime = checkIme && r.Count > 0 ? ImeRegistration.ReadUserDictionary() : null;
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
