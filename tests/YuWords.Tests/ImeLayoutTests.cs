using System.Runtime.InteropServices;
using YuWords.Core;

namespace YuWords.Tests;

/// <summary>IME の GetWords が返す領域の読み取り（IME の中の並びが、詰める・詰めないのどちらでも読めること）</summary>
public sealed class ImeLayoutTests
{
    static readonly Dictionary<ushort, string> Names = new() { [100] = "名詞", [106] = "人名" };

    /// <summary>stride ごとに IMEWRD を並べ、その後ろに文字を置いた領域を作る</summary>
    static IntPtr Build(int stride, (string R, string D, ushort P)[] words, int size, bool stringsOutside = false)
    {
        var buf = Marshal.AllocCoTaskMem(size);
        for (int i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
        int text = stride * words.Length;
        IntPtr Put(string s)
        {
            var p = buf + text;
            foreach (var ch in s + "\0") { Marshal.WriteInt16(buf, text, (short)ch); text += 2; }
            return stringsOutside ? new IntPtr(1) : p;   // 領域の外を指すおかしな値
        }
        for (int i = 0; i < words.Length; i++)
        {
            var e = buf + i * stride;
            Marshal.WriteIntPtr(e, 0, Put(words[i].R));
            Marshal.WriteIntPtr(e, IntPtr.Size, Put(words[i].D));
            Marshal.WriteInt16(e, IntPtr.Size * 2, (short)words[i].P);
        }
        return buf;
    }

    static readonly (string, string, ushort)[] Words = [("こうふ", "工符", 100), ("たかだ", "高田", 106), ("ずしめ", "図締", 100)];

    [Fact]
    public void 詰めない並びの領域を読める() => Check(ImeDictionary.NaturalWordSize);

    [Fact]
    public void 詰めた並びの領域を読める() => Check(ImeDictionary.PackedWordSize);

    static void Check(int stride)
    {
        var buf = Build(stride, Words, 4096);
        try
        {
            Assert.Equal([("こうふ", "工符", "名詞"), ("たかだ", "高田", "人名"), ("ずしめ", "図締", "名詞")],
                ImeDictionary.ParseWords(buf, 4096, Words.Length, Names));
        }
        finally { Marshal.FreeCoTaskMem(buf); }
    }

    [Fact]
    public void 文字の位置が領域の外なら読まない()
    {
        var buf = Build(ImeDictionary.NaturalWordSize, Words, 4096, stringsOutside: true);
        try
        {
            Assert.Empty(ImeDictionary.ParseWords(buf, 4096, Words.Length, Names));   // 落ちずに 0 語
        }
        finally { Marshal.FreeCoTaskMem(buf); }
    }
}
