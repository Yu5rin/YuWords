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
            using var dlg = new SettingsForm(new AppStore(dir));
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
        shotStore.Settings.Welcomed = true;
        var form = new MainForm(shotStore);
        form.Shown += async (_, _) =>
        {
            // 「;」で区切ると、2つ目以降は「今の一覧に追加」で読み込む
            foreach (var (path, i) in mailsPath.Split(';').Select((p, i) => (p, i)))
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
