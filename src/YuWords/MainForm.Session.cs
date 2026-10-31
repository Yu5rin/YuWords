using System.Diagnostics;
using YuWords.Core;

namespace YuWords;

// 確認画面の途中保存と、元に戻す
sealed partial class MainForm
{
    // ===== 途中保存 =====

    void RestoreSession()
    {
        var s = ReviewSession.Load(store.Dir);
        if (s == null || s.Items.Count == 0) return;
        all = s.ToItems(store.IsRegistered);
        restoredSources = s.Sources;
        grid.Columns[(int)Col.MailCount].HeaderText = s.CountHeader;
        grid.Columns[(int)Col.Signature].HeaderText = s.SignatureHeader;
        chkSig.Text = s.SignatureHeader == "署名" ? "署名由来のみ" : "定型行由来のみ";
        Text = $"YuWords - {string.Join(" ＋ ", s.Sources)}";
        Refilter();
        SetStatus($"前回の続きを開きました（{s.SavedAt:M/d H:mm} 保存、{all.Count} 語）。新しく読み込むと置き換わります。");
    }

    /// <summary>少し待ってから途中の状態を保存する</summary>
    void ScheduleSave()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    /// <summary>今の語句・よみで、登録済みかどうかを付け直す</summary>
    void RefreshRegistered()
    {
        foreach (var i in all) i.IsRegistered = i.ComputeRegistered(store.IsRegistered);
    }

    void SaveSession()
    {
        try
        {
            store.SyncHeld(all);   // 保留中の語の選択を held.json にも残す
            if (all.Count == 0) { ReviewSession.Delete(store.Dir); return; }
            var s = ReviewSession.From(all);
            s.Sources = loaded.Count > 0 ? loaded.Select(l => l.Name).ToList() : restoredSources;
            s.CountHeader = grid.Columns[(int)Col.MailCount].HeaderText;
            s.SignatureHeader = grid.Columns[(int)Col.Signature].HeaderText;
            s.Save(store.Dir);
        }
        catch (Exception)
        {
            // 保存できなくても作業は続けられる
        }
    }

    void ClearList()
    {
        if (all.Count == 0 || cts != null) return;
        if (all.Any(i => i.Register && !i.IsRegistered) &&
            MessageBox.Show(this, "登録チェックを付けた語が、まだ登録も出力もされていません。一覧を閉じますか？", "YuWords",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;
        all = [];
        loaded = [];
        restoredSources = [];
        undo.Clear();
        Text = "YuWords";
        Refilter();
        SaveSession();
        SetStatus("一覧を閉じました。");
    }

    // ===== 元に戻す =====

    /// <summary>行の今の値を控え、元に戻せるようにする</summary>
    void PushUndo(string label, IEnumerable<ReviewItem> items)
    {
        var saved = items.Select(i => (Item: i, State: i.Save())).ToList();
        if (saved.Count == 0) return;
        var restoreLearned = LearnedRestorer(saved.Select(x => x.Item));
        PushUndo(label, () =>
        {
            foreach (var (i, st) in saved) i.Restore(st);
            restoreLearned();
        });
    }

    /// <summary>今の「覚えたよみ」を控え、元に戻す処理を返す（よみを直す操作を元に戻したときに、覚えたよみも戻す）</summary>
    Action LearnedRestorer(IEnumerable<ReviewItem> items)
    {
        var saved = items.Select(i => i.Source.Word).Distinct()
            .Select(w => (Word: w, Reading: store.Learned.GetValueOrDefault(w, ""))).ToList();
        return () =>
        {
            foreach (var (w, r) in saved)
                if (store.Learned.GetValueOrDefault(w, "") != r) store.Learn(w, r);   // 空なら記録を消す
        };
    }

    void PushUndo(string label, Action action)
    {
        undo.Push((label, action));
        while (undo.Count > 100) TrimUndo();
    }

    void TrimUndo()
    {
        var keep = undo.Reverse().Skip(1).ToList();
        undo.Clear();
        foreach (var u in keep) undo.Push(u);
    }

    void Undo()
    {
        if (grid.IsCurrentCellInEditMode) return;
        if (!undo.TryPop(out var u))
        {
            SetStatus("元に戻せる操作はありません。");
            return;
        }
        u.Undo();
        RefreshRegistered();
        Refilter();
        ScheduleSave();
        SetStatus($"元に戻しました: {u.Label}");
    }

    ImeConverter UiIme => uiIme ??= new ImeConverter();

    /// <summary>よみを変えた行の変換結果を取り直す</summary>
    void Reconvert(ReviewItem i) => i.ImeConversion = UiIme.Convert(i.Reading);

    void AdoptImeReading(IEnumerable<ReviewItem> items)
    {
        var targets = items.Where(i => i.ReadingsDiffer).ToList();
        if (targets.Count == 0) return;
        PushUndo("IME のよみを採用", targets);
        foreach (var i in targets)
        {
            i.EditReading(i.ImeReading);
            i.Reviewed = true;
            Reconvert(i);
            store.Learn(i.Source.Word, i.Reading);
        }
        AfterEdit();
        SetStatus($"{targets.Count} 語のよみを IME のよみにしました（Ctrl+Z で元に戻せます）。");
    }

    void AddShortReadings()
    {
        var items = SelectedItems().Where(i => i.ShortReading.Length > 0).ToList();
        if (items.Count == 0)
        {
            SetStatus("短いよみの提案がある語（2語以上からなる長い語）を選んでください。");
            return;
        }
        PushUndo("短いよみを付ける", items);
        int n = items.Count(i => i.AddShortReading());
        foreach (var i in items) i.Reviewed = true;
        AfterEdit();
        SetStatus($"{n} 語に短いよみを付けました。登録すると、短いよみでも変換できます（Ctrl+Z で元に戻せます）。");
    }

    /// <summary>選んだ語の「表記=よみ」をよみのルールに足す。次回から、その語を含む複合語にも使う</summary>
    void MakeRules()
    {
        var items = SelectedItems().Where(i => i.Reading.Length > 0 && i.Category != CandidateBuilder.PhraseCategory).ToList();
        if (items.Count == 0) return;
        var rules = store.Settings.Extractor.ReadingRules;
        var map = TermExtractor.ParseRules(rules);
        int n = 0;
        foreach (var i in items)
        {
            if (map.TryGetValue(i.Word, out var r) && r == i.Reading) continue;
            rules.RemoveAll(l => TermExtractor.ParseRules([l]).ContainsKey(i.Word));
            rules.Add($"{i.Word}={i.Reading}");
            n++;
        }
        store.SaveSettings();
        SetStatus(n == 0 ? "選んだ語は、すでに同じよみでルールにあります。"
            : $"{n} 語をよみのルールに足しました。次の読み込みから、その語を含む複合語にもこのよみを使います（［設定］→［よみのルール］で直せます）。");
    }

    void SetReviewed(bool on)
    {
        var items = SelectedItems().Where(i => i.Reviewed != on).ToList();
        if (items.Count == 0) return;
        PushUndo(on ? "確認済み" : "未確認に戻す", items);
        foreach (var i in items) i.Reviewed = on;
        AfterEdit();
    }

    void SetHeld(bool on)
    {
        var items = SelectedItems().Where(i => i.Held != on).ToList();
        if (items.Count == 0) return;
        PushUndo(on ? "保留" : "保留を解除", items);
        foreach (var i in items)
        {
            (i.Held, i.Reviewed) = (on, true);
            if (on) i.Register = false;   // 保留した語は登録しない
        }
        store.SyncHeld(items);
        Refilter();
        ScheduleSave();
        SetStatus(on
            ? $"{items.Count} 語を保留にしました。次回以降も、選んだよみのまま保留として残ります（「保留中も表示」で見られます）。"
            : $"{items.Count} 語の保留を解除しました。");
    }

    void SetPos(string pos)
    {
        var items = SelectedItems();
        if (items.Count == 0) return;
        PushUndo($"品詞を{pos}に", items);
        foreach (var i in items) (i.Pos, i.Reviewed) = (pos, true);
        AfterEdit();
    }

    /// <summary>行の値を変えたあとの共通処理（表記ゆれの付け直し・再描画・自動保存）</summary>
    void AfterEdit()
    {
        RefreshRegistered();
        ReviewItem.MarkVariants(all);
        ReviewItem.Rank(all);
        grid.Invalidate();
        UpdateCounts();
        ScheduleSave();
    }
}
