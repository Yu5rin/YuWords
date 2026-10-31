namespace YuWords;

/// <summary>画面の拡大率（125%・200% など）に合わせて、コードで決めた大きさを広げる</summary>
static class Dpi
{
    static readonly int dpi = Read();

    static int Read()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return (int)g.DpiX;
    }

    public static int Scale(int pixels) => pixels * dpi / 96;
}
