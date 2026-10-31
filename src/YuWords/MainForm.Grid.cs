using System.Diagnostics;
using YuWords.Core;

namespace YuWords;

// 確認画面の表（値の受け渡し・色・キー操作）と一覧の操作
sealed partial class MainForm
{
    // ===== グリッドの値 =====

    void Grid_CellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex >= view.Count) return;
        var i = view[e.RowIndex];
        e.Value = (Col)e.ColumnIndex switch
        {
            Col.Register => i.Register,
            Col.Check => i.Held ? "保留" : i.Reviewed ? "✓" : "",
            Col.Stars => new string('★', i.Stars),
            Col.Word => i.Word,
            Col.Reading => i.Reading,
            Col.ImeReading => i.ImeReading,
            Col.Extra => string.Join("、", i.ExtraReadings),
            Col.Pos => i.Pos,
            Col.MailCount => i.MailCount,
            Col.Category => i.Category,
            Col.Signature => i.FromSignature ? "○" : "",
            Col.Ime => i.ImeConversion,
            Col.Unsure => i.Unsure ? "要確認" : "",
            Col.Example => i.Example,
            _ => null,
        };
    }

    void Grid_CellValuePushed(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex >= view.Count) return;
        var i = view[e.RowIndex];
        var before = i.Save();
        var restoreLearned = LearnedRestorer([i]);
        switch ((Col)e.ColumnIndex)
        {
            case Col.Register: i.Register = e.Value is true; break;
            case Col.Word:
                var w = (e.Value as string ?? "").Trim();
                if (w.Length > 0) i.Word = w;
                break;
            case Col.Reading:
                i.EditReading(e.Value as string ?? "");
                if (i.Reading != before.Reading)
                {
                    Reconvert(i);   // 直したよみで正しく変換されるかをすぐ見せる
                    store.Learn(i.Source.Word, i.Reading);   // 次回の抽出でも同じよみを使う
                }
                break;
            case Col.Pos: i.Pos = e.Value as string ?? "名詞"; break;
            case Col.Extra:
                i.SetExtraReadings(e.Value as string ?? "");
                if (i.ExtraReadings.Count > 0 && i.Reading.Length > 0) i.Register = true;
                break;
        }
        if (i.Save() != before)
        {
            i.Reviewed = true;
            var header = grid.Columns[e.ColumnIndex].HeaderText;
            PushUndo($"「{before.Word}」の{header}", () => { i.Restore(before); restoreLearned(); });
        }
        AfterEdit();
    }

    void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex >= view.Count || e.CellStyle == null) return;
        var i = view[e.RowIndex];
        var t = Theme.Current;
        if (i.IsRegistered)
            e.CellStyle.ForeColor = t.Muted;   // 以前に登録した語は薄く
        if ((Col)e.ColumnIndex == Col.Reading)
        {
            if (i.Reading.Length == 0) e.CellStyle.BackColor = t.Unknown;   // よみ不明
            else if (i.Unsure) e.CellStyle.BackColor = t.Unsure;           // 要確認
        }
        if ((Col)e.ColumnIndex == Col.Ime && i.ImeConvertible)
            e.CellStyle.ForeColor = t.Good;
        if ((Col)e.ColumnIndex == Col.ImeReading)
        {
            if (i.ReadingsDiffer) e.CellStyle.BackColor = t.ImeDiff;   // 推測と食い違う
            else e.CellStyle.ForeColor = t.Muted;
        }
        if ((Col)e.ColumnIndex == Col.Word && i.Variants.Count > 0)
            e.CellStyle.BackColor = t.Variant;   // 表記ゆれ
    }

    void Grid_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
        var i = view[e.RowIndex];
        e.ToolTipText = (Col)e.ColumnIndex switch
        {
            Col.Example => i.Example,
            Col.ImeReading when i.ReadingsDiffer => "IME に表記を渡したときのよみです。推測と違います。ダブルクリックでこのよみにします",
            Col.ImeReading => "IME に表記を渡したときのよみです",
            Col.Extra when i.ShortReading.Length > 0 && !i.ExtraReadings.Contains(i.ShortReading) =>
                $"本来のよみのほかに登録するよみ（「、」区切り）。短いよみの提案: {i.ShortReading}（右クリック「短いよみを付ける」）",
            Col.Extra => "本来のよみのほかに登録するよみ（「、」区切り）。短い入力で呼び出せます",
            Col.Reading when i.Category == CandidateBuilder.PhraseCategory && i.Reading.Length == 0 =>
                "定型文を呼び出す短いよみを入れてください（例: しょめい、いつも）",
            Col.Word when i.Variants.Count > 0 => $"同じよみで表記が違う候補があります: {string.Join("、", i.Variants)}",
            Col.Reading when i.Reading.Length == 0 => "よみを推測できませんでした。入力すると登録できます",
            Col.Reading when i.Unsure => "よみは推測です。確認して直してください",
            Col.Reading when i.Source.ReadingSource == "前回の修正" && i.Reading == i.Source.Reading => "よみは前回自分で直したものです",
            Col.Reading when i.Source.ReadingSource.Length > 0 && i.Reading == i.Source.Reading => $"よみは{i.Source.ReadingSource}のふりがなです",
            Col.Stars => "登録する価値の目安（IME で変換できない語・よく使う語ほど高い）",
            _ when i.InImeDictionary => "IME のユーザー辞書に既にある語です",
            _ when i.IsRegistered => "以前に登録した語です",
            _ => "",
        };
    }

    void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && grid.CurrentCell?.ColumnIndex != (int)Col.Register && !grid.IsCurrentCellInEditMode)
        {
            // スペースで選択行の登録を切り替える
            var rows = SelectedItems();
            if (rows.Count == 0) return;
            SetRegister(!rows.All(r => r.Register));
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Delete && !grid.IsCurrentCellInEditMode)
        {
            ExcludeSelected();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None && !grid.IsCurrentCellInEditMode)
        {
            // Enter で見終えた行を確認済みにして、次の行へ進む（移動は既定の動きに任せる）
            SetReviewed(true);
        }
        else if (e.KeyCode == Keys.Z && e.Control && !grid.IsCurrentCellInEditMode)
        {
            Undo();
            e.Handled = true;
        }
    }

    List<ReviewItem> SelectedItems() =>
        grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex)
            .Where(r => r >= 0 && r < view.Count).Distinct().OrderBy(r => r).Select(r => view[r]).ToList();

    // ===== 例文の欄 =====

    ReviewItem? exampleShown;

    /// <summary>今の行の語の例文（最大 5 行）を、語を強調して表の下に出す</summary>
    void ShowExamples()
    {
        var row = grid.CurrentCell?.RowIndex ?? -1;
        var item = row >= 0 && row < view.Count ? view[row] : null;
        if (item == exampleShown && item != null) return;
        exampleShown = item;
        rtbExamples.Clear();
        if (item == null) return;
        var bold = new Font(rtbExamples.Font, FontStyle.Bold);
        rtbExamples.SelectionColor = Theme.Current.Muted;
        rtbExamples.AppendText($"「{item.Source.Word}」の例文（{item.Examples.Count} 件）\n");
        foreach (var line in item.Examples)
        {
            int start = rtbExamples.TextLength;
            rtbExamples.SelectionColor = Theme.Current.Fore;
            rtbExamples.AppendText("・" + line + "\n");
            for (int p = line.IndexOf(item.Source.Word, StringComparison.Ordinal); p >= 0 && item.Source.Word.Length > 0;
                 p = line.IndexOf(item.Source.Word, p + item.Source.Word.Length, StringComparison.Ordinal))
            {
                rtbExamples.Select(start + 1 + p, item.Source.Word.Length);
                rtbExamples.SelectionBackColor = Theme.Current.Highlight;
                rtbExamples.SelectionFont = bold;
            }
        }
        rtbExamples.Select(0, 0);
    }

    // ===== 一括操作 =====

    void BuildBulkMenu()
    {
        btnBulk.ToolTipText = "表示中の行にまとめて行う操作";
        btnBulk.DropDownItems.Add("表示中の行をすべて確認済みにする", null, (_, _) => Bulk("確認済み", i => i.Reviewed = true));
        btnBulk.DropDownItems.Add("表示中の行の登録チェックをすべて ON", null, (_, _) => Bulk("登録 ON", i => (i.Register, i.Reviewed) = (i.Reading.Length > 0 && !i.IsRegistered && !i.Held, true)));
        btnBulk.DropDownItems.Add("表示中の行の登録チェックをすべて OFF", null, (_, _) => Bulk("登録 OFF", i => i.Register = false));
        btnBulk.DropDownItems.Add(new ToolStripSeparator());
        btnBulk.DropDownItems.Add("IME で変換できる語の登録チェックを外す", null, (_, _) =>
            Bulk("IME で変換できる語の登録を外す", i => { if (i.ImeConvertible) i.Register = false; }));
        btnBulk.DropDownItems.Add("IME で変換できる語を表示から外す（除外リストへ）", null, (_, _) => ExcludeConvertible());
        btnBulk.DropDownItems.Add(new ToolStripSeparator());
        btnBulk.DropDownItems.Add("選んだ行のよみを置き換える...", null, (_, _) => ReplaceReadings());
        btnBulk.DropDownItems.Add("今の設定で数え直す", null, async (_, _) => await RecountAsync());
    }

    /// <summary>表示中の行すべてに同じ操作をする（元に戻せる）</summary>
    void Bulk(string label, Action<ReviewItem> action)
    {
        if (view.Count == 0) return;
        var items = view.ToList();
        PushUndo($"{label}（{items.Count} 行）", items);
        foreach (var i in items) action(i);
        AfterEdit();
        SetStatus($"表示中の {items.Count} 行に「{label}」を行いました（Ctrl+Z で元に戻せます）。");
    }

    void ExcludeConvertible()
    {
        var items = view.Where(i => i.ImeConvertible && !i.IsRegistered && i.Category != CandidateBuilder.PhraseCategory).ToList();
        if (items.Count == 0)
        {
            SetStatus("表示中の行に、IME で変換できる語はありません。");
            return;
        }
        if (MessageBox.Show(this, $"表示中の行のうち、IME がすでに正しく変換できる {items.Count} 語を除外リストに入れます。\n" +
                "（登録すると予測変換に出やすくなる語もあります。必要なものは先に登録してください）",
                "一括操作", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        grid.ClearSelection();
        foreach (var i in items)
        {
            var r = view.IndexOf(i);
            if (r >= 0) grid.Rows[r].Cells[(int)Col.Word].Selected = true;
        }
        ExcludeSelected();
    }

    void ReplaceReadings()
    {
        var items = SelectedItems();
        if (items.Count == 0) items = view.ToList();
        using var f = new ReplaceReadingForm(items.Count);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        PushUndo($"よみの置き換え（{f.From} → {f.To}）", items);
        int n = 0;
        foreach (var i in items)
        {
            if (!i.ReplaceInReading(f.From, f.To)) continue;
            Reconvert(i);
            store.Learn(i.Source.Word, i.Reading);
            i.Reviewed = true;
            n++;
        }
        AfterEdit();
        SetStatus($"{n} 語のよみを置き換えました（Ctrl+Z で元に戻せます）。");
    }

    // ===== 一覧の操作 =====

    void Refilter()
    {
        ReviewItem.MarkVariants(all);
        ReviewItem.Rank(all);
        view = all.Where(filter.Matches).ToList();
        ApplySort();
        grid.RowCount = 0;
        grid.RowCount = view.Count;
        grid.Invalidate();
        UpdateCounts();
    }

    void SortBy(Col col)
    {
        if (sortCol == col) sortDesc = !sortDesc;
        else { sortCol = col; sortDesc = col is Col.MailCount or Col.Register or Col.Signature or Col.Unsure; }
        foreach (DataGridViewColumn c in grid.Columns) c.HeaderCell.SortGlyphDirection = SortOrder.None;
        grid.Columns[(int)col].HeaderCell.SortGlyphDirection = sortDesc ? SortOrder.Descending : SortOrder.Ascending;
        ApplySort();
        grid.Invalidate();
    }

    void ApplySort()
    {
        if (sortCol is not { } col) return;
        Func<ReviewItem, IComparable> key = col switch
        {
            Col.Register => i => i.Register,
            Col.Check => i => i.Held ? 2 : i.Reviewed ? 1 : 0,
            Col.Stars => i => i.Score,
            Col.Word => i => i.Word,
            Col.Reading => i => i.Reading,
            Col.ImeReading => i => i.ImeReading,
            Col.Extra => i => string.Join("、", i.ExtraReadings),
            Col.Pos => i => i.Pos,
            Col.MailCount => i => i.MailCount,
            Col.Category => i => i.Category,
            Col.Signature => i => i.FromSignature,
            Col.Ime => i => i.ImeConversion,
            Col.Unsure => i => i.Unsure,
            _ => i => i.Example,
        };
        // 同じ値の中では出現メール数の多い順
        view = (sortDesc ? view.OrderByDescending(key, Comparer<IComparable>.Default)
                         : view.OrderBy(key, Comparer<IComparable>.Default))
               .ThenByDescending(i => i.MailCount).ToList();
    }

    void SetRegister(bool on)
    {
        var items = SelectedItems();
        PushUndo(on ? "登録 ON" : "登録 OFF", items);
        foreach (var i in items) (i.Register, i.Reviewed) = (on && i.Reading.Length > 0, true);
        AfterEdit();
    }

    void ExcludeSelected()
    {
        var items = SelectedItems();
        if (items.Count == 0) return;
        // 確認は出さず、元に戻せるようにする
        var words = items.Select(i => i.Source.Word).Where(w => !store.Excluded.Contains(w)).ToList();
        store.AddExcluded(words);
        var positions = items.Select(i => (Item: i, Index: all.IndexOf(i))).OrderBy(x => x.Index).ToList();
        var set = items.ToHashSet();
        all.RemoveAll(set.Contains);
        PushUndo(items.Count == 1 ? $"「{items[0].Word}」を除外" : $"{items.Count} 語を除外", () =>
        {
            foreach (var w in words) store.Excluded.Remove(w);
            store.SaveExcluded();
            foreach (var (item, index) in positions) all.Insert(Math.Min(index, all.Count), item);
        });
        Refilter();
        ScheduleSave();
        SetStatus((items.Count == 1 ? $"「{items[0].Word}」" : $"{items.Count} 語") +
                  "を除外リストに追加しました。次回以降の抽出結果にも出ません（Ctrl+Z で元に戻せます）。");
    }

    void UpdateCounts()
    {
        int chk = all.Count(i => i.Register && !i.Held);
        int unreviewed = all.Count(i => !i.Reviewed && !i.IsRegistered && !i.Held);
        counts.Text = $"表示 {view.Count} / 候補 {all.Count} 語　未確認 {unreviewed} 語　登録チェック {chk} 語";
        btnExportIme.Enabled = btnRegister.Enabled = chk > 0 && cts == null;
        btnExportCsv.Enabled = all.Count > 0 && cts == null;
    }

    void SetStatus(string text) => status.Text = text;
}
