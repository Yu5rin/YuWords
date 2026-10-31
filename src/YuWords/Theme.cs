using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace YuWords;

/// <summary>
/// 画面の色（明るい／暗い）。「自動」なら Windows のアプリのモード（設定 → 個人用設定 → 色）に合わせる。
/// 一覧の印の色（推測・不明・食い違い・表記ゆれ など）も、暗い画面で読める色に切り替える
/// </summary>
sealed class Theme
{
    public required bool Dark { get; init; }
    public required Color Back { get; init; }
    public required Color Fore { get; init; }
    public required Color Panel { get; init; }
    public required Color Input { get; init; }
    public required Color GridLine { get; init; }
    public required Color Selection { get; init; }
    public required Color Unsure { get; init; }
    public required Color Unknown { get; init; }
    public required Color ImeDiff { get; init; }
    public required Color Variant { get; init; }
    public required Color Good { get; init; }
    public required Color Muted { get; init; }
    public required Color Star { get; init; }
    public required Color Highlight { get; init; }

    public static readonly Theme Light = new()
    {
        Dark = false, Back = SystemColors.Window, Fore = SystemColors.WindowText, Panel = SystemColors.Control,
        Input = SystemColors.Window, GridLine = SystemColors.ControlDark, Selection = SystemColors.Highlight,
        Unsure = Color.FromArgb(255, 243, 196), Unknown = Color.FromArgb(255, 214, 214), ImeDiff = Color.FromArgb(255, 226, 190),
        Variant = Color.FromArgb(222, 235, 255), Good = Color.FromArgb(0, 128, 0), Muted = SystemColors.GrayText,
        Star = Color.FromArgb(214, 140, 0), Highlight = Color.FromArgb(255, 236, 150),
    };

    public static readonly Theme DarkTheme = new()
    {
        Dark = true, Back = Color.FromArgb(30, 30, 30), Fore = Color.FromArgb(232, 232, 232), Panel = Color.FromArgb(43, 43, 43),
        Input = Color.FromArgb(51, 51, 51), GridLine = Color.FromArgb(70, 70, 70), Selection = Color.FromArgb(38, 79, 120),
        Unsure = Color.FromArgb(92, 80, 28), Unknown = Color.FromArgb(105, 40, 40), ImeDiff = Color.FromArgb(110, 66, 22),
        Variant = Color.FromArgb(34, 56, 92), Good = Color.FromArgb(120, 210, 120), Muted = Color.FromArgb(140, 140, 140),
        Star = Color.FromArgb(240, 180, 50), Highlight = Color.FromArgb(110, 95, 25),
    };

    public static Theme Current { get; private set; } = Light;

    /// <summary>設定（auto / light / dark）から色を決める</summary>
    public static void Select(string mode) =>
        Current = mode switch
        {
            "dark" => DarkTheme,
            "light" => Light,
            _ => WindowsUsesDark() ? DarkTheme : Light,
        };

    static bool WindowsUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception) { return false; }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>画面とその中の部品に色を当てる（暗いときはタイトルバーも暗くする）</summary>
    public static void Apply(Form form)
    {
        var t = Current;
        form.HandleCreated += (_, _) =>
        {
            int on = t.Dark ? 1 : 0;
            try { DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)); } catch (Exception) { }   // DWMWA_USE_IMMERSIVE_DARK_MODE
        };
        if (!t.Dark) return;
        // 部品がそろってから色を当てる（コンストラクタの途中で呼んでもよい）
        form.Load += (_, _) =>
        {
            form.BackColor = t.Panel;
            form.ForeColor = t.Fore;
            ApplyTo(form.Controls, t);
        };
    }

    static Icon? icon;

    /// <summary>アプリのアイコン（exe に埋め込んだもの）</summary>
    public static Icon? AppIcon
    {
        get
        {
            try { return icon ??= Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { return null; }
        }
    }

    static void ApplyTo(Control.ControlCollection controls, Theme t)
    {
        foreach (Control c in controls)
        {
            switch (c)
            {
                case DataGridView g:
                    g.EnableHeadersVisualStyles = false;
                    g.BackgroundColor = t.Back;
                    g.GridColor = t.GridLine;
                    g.DefaultCellStyle.BackColor = t.Back;
                    g.DefaultCellStyle.ForeColor = t.Fore;
                    g.DefaultCellStyle.SelectionBackColor = t.Selection;
                    g.DefaultCellStyle.SelectionForeColor = Color.White;
                    g.ColumnHeadersDefaultCellStyle.BackColor = t.Panel;
                    g.ColumnHeadersDefaultCellStyle.ForeColor = t.Fore;
                    g.ColumnHeadersDefaultCellStyle.SelectionBackColor = t.Panel;
                    g.RowHeadersDefaultCellStyle.BackColor = t.Panel;
                    g.RowHeadersDefaultCellStyle.SelectionBackColor = t.Selection;
                    break;
                case TextBoxBase or ListView or ComboBox or NumericUpDown:
                    c.BackColor = t.Input;
                    c.ForeColor = t.Fore;
                    break;
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = t.GridLine;
                    b.BackColor = t.Input;
                    b.ForeColor = t.Fore;
                    break;
                case ToolStrip ts:
                    ts.Renderer = new ToolStripProfessionalRenderer(new DarkColors(t));
                    ts.BackColor = t.Panel;
                    ts.ForeColor = t.Fore;
                    foreach (ToolStripItem item in ts.Items) Paint(item, t);
                    break;
                case TabControl:
                    break;   // タブの見出しは OS が描くのでそのまま
                default:
                    c.BackColor = c is TabPage or System.Windows.Forms.Panel or Splitter ? t.Panel : c.BackColor;
                    c.ForeColor = t.Fore;
                    break;
            }
            if (c.ContextMenuStrip is { } menu)
            {
                menu.Renderer = new ToolStripProfessionalRenderer(new DarkColors(t));
                foreach (ToolStripItem item in menu.Items) Paint(item, t);
            }
            if (c.HasChildren) ApplyTo(c.Controls, t);
        }
    }

    static void Paint(ToolStripItem item, Theme t)
    {
        item.ForeColor = t.Fore;
        if (item is ToolStripDropDownItem dd)
        {
            dd.DropDown.BackColor = t.Panel;
            foreach (ToolStripItem sub in dd.DropDownItems) Paint(sub, t);
        }
    }

    sealed class DarkColors(Theme t) : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => t.Panel;
        public override Color ToolStripGradientMiddle => t.Panel;
        public override Color ToolStripGradientEnd => t.Panel;
        public override Color ToolStripBorder => t.Panel;
        public override Color StatusStripGradientBegin => t.Panel;
        public override Color StatusStripGradientEnd => t.Panel;
        public override Color MenuItemSelected => t.Selection;
        public override Color MenuItemSelectedGradientBegin => t.Selection;
        public override Color MenuItemSelectedGradientEnd => t.Selection;
        public override Color MenuItemBorder => t.Selection;
        public override Color MenuItemPressedGradientBegin => t.Input;
        public override Color MenuItemPressedGradientEnd => t.Input;
        public override Color ButtonSelectedHighlight => t.Selection;
        public override Color ButtonSelectedGradientBegin => t.Selection;
        public override Color ButtonSelectedGradientEnd => t.Selection;
        public override Color ButtonPressedGradientBegin => t.Input;
        public override Color ButtonPressedGradientEnd => t.Input;
        public override Color ButtonCheckedGradientBegin => t.Input;
        public override Color ButtonCheckedGradientEnd => t.Input;
        public override Color ToolStripDropDownBackground => t.Panel;
        public override Color ImageMarginGradientBegin => t.Panel;
        public override Color ImageMarginGradientMiddle => t.Panel;
        public override Color ImageMarginGradientEnd => t.Panel;
        public override Color SeparatorDark => t.GridLine;
        public override Color SeparatorLight => t.Panel;
        public override Color MenuBorder => t.GridLine;
    }
}
