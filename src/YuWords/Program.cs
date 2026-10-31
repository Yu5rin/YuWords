using YuWords.Core;

namespace YuWords;

static class Program
{
    /// <summary>
    /// 引数なしで通常起動。
    /// YuWords.exe --check-new … 新しいメールの差分を調べて、候補があれば通知する（タスク スケジューラ用）
    /// YuWords.exe --new       … 起動して、前回以降の新しいメールだけを読み込む（通知から開いたとき）
    /// YuWords.exe --screenshot &lt;メールのテキスト|フォルダ|settings&gt; &lt;出力.png&gt; … 一時フォルダの設定で読み込み、画面を画像に保存して終了（画面確認用）
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // IME のユーザー辞書を使う処理だけを行う別のプロセス（本体から起動される）
        if (args.Length == 3 && args[0] == ImeWorker.Argument)
        {
            Environment.Exit(ImeWorker.RunFromFiles(args[1], args[2]));
            return;
        }
        ImeWorker.ExecutablePath = Application.ExecutablePath;
        AppStore.DefaultsPath = Path.Combine(AppContext.BaseDirectory, AppStore.DefaultsFileName);
        ApplicationConfiguration.Initialize();
        if (args.Length == 3 && args[0] == "--screenshot")
        {
            Screenshot(args[1], args[2]);
            return;
        }

        AppStore store;
        if (args.Length == 1 && args[0] == AutoCheck.CheckArgument)
        {
            // タスク スケジューラからの自動チェック（画面は出さない）
            try
            {
                store = new AppStore();
                Log.Init(store.Dir);
                ImeDictionary.CrashMarkerPath = Path.Combine(store.Dir, "ime_dictionary_in_use");
                ImeRegistration.Enabled = store.Settings.UseImeDictionary;
                Application.Run(new CheckNewRunner(store));
            }
            catch (Exception e) { Log.Error("自動チェックを始められませんでした", e); }
            return;
        }
        try
        {
            MorphTokenizer.FindDictionary();   // 辞書が無ければ起動時に知らせる
            store = new AppStore();
            Theme.Select(store.Settings.ThemeMode);
            Log.Init(store.Dir);
            Log.Info($"起動 {Application.ProductVersion}");
            ImeDictionary.CrashMarkerPath = Path.Combine(store.Dir, "ime_dictionary_in_use");
            if (ImeDictionary.CrashedLastTime())
            {
                // 前回、IME のユーザー辞書を使っている最中に落ちた。同じことを繰り返さないよう直接使うのをやめる
                Log.Error("前回、IME のユーザー辞書を使っている最中に終了したため、直接使う設定を OFF にしました");
                store.Settings.UseImeDictionary = false;
                store.SaveSettings();
                MessageBox.Show(
                    "前回、IME のユーザー辞書を使っている最中に YuWords が止まりました。\n\n" +
                    "同じことが起きないよう、IME のユーザー辞書を直接使う機能（直接登録・登録済みの照合）を OFF にしました。\n" +
                    "登録は「辞書ファイルに出力」してユーザー辞書ツールで取り込めます。\n\n" +
                    "［設定］→［抽出］の「IME のユーザー辞書を直接使う」で ON に戻せます。",
                    "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            ImeRegistration.Enabled = store.Settings.UseImeDictionary;
            ImeWorker.CleanUpOldFiles();
            Application.ThreadException += (_, e) =>
            {
                Log.Error("予期しないエラー", e.Exception);
                MessageBox.Show(e.Exception.Message, "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "YuWords", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var main = new MainForm(store);
        if (args.FirstOrDefault(a => a is "--updated" or "--update-failed" or "--rolled-back") is { } updateArg)
            main.ShowUpdateResult(updateArg);   // 更新のあとに起動し直したとき
        if (args.Contains(MainForm.NewOnlyArgument)) main.LoadNewOnShown();   // 通知から開いたとき
        Application.Run(main);
    }

    static void Screenshot(string mailsPath, string pngPath)
    {
        Theme.Select(Environment.GetEnvironmentVariable("YUWORDS_THEME") ?? "light");
        var dir = Path.Combine(Path.GetTempPath(), $"yuwords_shot_{Guid.NewGuid():N}");
        if (mailsPath == "settings")
        {
            // 設定画面の各タブを横に並べて保存する
            // 登録済みの語の画面が空にならないよう、架空の語を入れておく
            var settingsStore = new AppStore(dir);
            settingsStore.AddRegistered([("XYZ設検課", "えっくすわいぜっとせつけんか", "固有名詞"), ("工符", "こうふ", "名詞"), ("図締", "ずしめ", "名詞"), ("高田", "たかだ", "人名")]);
            settingsStore.MarkUsed(["工符"], DateTime.Now);
            using var dlg = new SettingsForm(settingsStore);
            dlg.Shown += (_, _) =>
            {
                var tabs = dlg.Controls.OfType<TabControl>().First();
                using var bmp = new Bitmap(dlg.Width * tabs.TabCount, dlg.Height);
                for (int i = 0; i < tabs.TabCount; i++)
                {
                    tabs.SelectedIndex = i;
                    dlg.Refresh();
                    dlg.DrawToBitmap(bmp, new Rectangle(dlg.Width * i, 0, dlg.Width, dlg.Height));
                }
                bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                dlg.Close();
            };
            dlg.ShowDialog();
            Directory.Delete(dir, true);
            return;
        }
        if (mailsPath == "lookup")
        {
            // 用語を引く画面を、架空の語とメモで保存する
            var s = new AppStore(dir);
            s.AddRegistered([("MOP", "もっぷ", "名詞"), ("MOP", "えむおーぴー", "名詞"), ("図締", "ずしめ", "名詞"), ("XYZ設検課", "えっくすわいぜっとせつけんか", "固有名詞"), ("工符", "こうふ", "名詞")]);
            s.SetNote("MOP", "遠隔発報装置");
            s.SetNote("図締", "図面の締め切り");
            s.SetNote("工符", "工事の符号表");
            using var f = new LookupForm(s);
            f.Shown += (_, _) =>
            {
                using var bmp = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                f.Close();
            };
            f.ShowDialog();
            Directory.Delete(dir, true);
            return;
        }
        if (mailsPath == "stats")
        {
            // 効果の画面を、架空の登録済みの語で保存する
            var s = new AppStore(dir);
            string[] sample = ["XYZ設検課", "工符", "図締", "盤検", "検了", "桐電盤", "高田", "中野", "MOP", "拡メル", "リプレース", "設検課"];
            s.AddRegistered(sample.Select((w, i) => (w, $"よみ{i}", i is 6 or 7 ? "人名" : i == 8 ? "固有名詞" : "名詞")));
            for (int i = 0; i < s.Registered.Count; i++)
                s.Registered[i] = s.Registered[i] with { RegisteredAt = DateTime.Now.AddMonths(-(i % 5)).AddDays(-i), Rescued = i % 3 != 2 };
            s.MarkUsed(new Dictionary<string, int> { ["工符"] = 18, ["XYZ設検課"] = 12, ["図締"] = 9, ["盤検"] = 4, ["高田"] = 3 }, 120, DateTime.Now);
            using var f = new StatsForm(UsageStats.Compute(s, DateTime.Now));
            f.Shown += (_, _) =>
            {
                using var bmp = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                f.Close();
            };
            f.ShowDialog();
            Directory.Delete(dir, true);
            return;
        }
        if (Path.GetExtension(mailsPath).ToLowerInvariant() is ".csv" or ".xlsx")
        {
            // 列を選ぶ画面を保存する
            using var pick = new TableColumnForm(Path.GetFileName(mailsPath), DocumentSources.ReadTable(mailsPath));
            pick.Shown += (_, _) =>
            {
                using var bmp = new Bitmap(pick.Width, pick.Height);
                pick.DrawToBitmap(bmp, new Rectangle(Point.Empty, pick.Size));
                bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                pick.Close();
            };
            pick.ShowDialog();
            return;
        }
        // 画面確認では実機の IME 辞書を読まず、初回の案内も出さない
        var shotStore = new AppStore(dir);
        shotStore.Settings.CheckImeDictionary = false;
        shotStore.Settings.UseContactReadings = false;
        shotStore.Settings.UpdateCheck = false;
        shotStore.Settings.Welcomed = true;
        var form = new MainForm(shotStore);
        form.Shown += async (_, _) =>
        {
            // 「;」で区切ると、2つ目以降は「今の一覧に追加」で読み込む
            // 「empty」なら何も読み込まない（空の一覧の案内を確かめる）
            foreach (var (path, i) in mailsPath.Split(';').Where(p => p != "empty").Select((p, i) => (p, i)))
            {
                form.SetAppend(i > 0);
                await (Directory.Exists(path) ? form.LoadFolderAsync(path) : form.LoadTextAsync(path));
            }
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
            bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
            form.Close();
        };
        Application.Run(form);
        Directory.Delete(dir, true);
    }
}
