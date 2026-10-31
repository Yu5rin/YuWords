using System.Runtime.InteropServices;

namespace YuWords.Core;

/// <summary>1語の登録結果</summary>
public enum RegisterResult { Registered, Exists, Failed }

/// <summary>
/// Microsoft IME のユーザー辞書（IFEDictionary）への直接登録。定義は Windows SDK の msime.h に合わせている。
/// IME の中の構造体の並び（詰めるかどうか）は外からは確かめられないので、どちらの並びでも正しく伝わる渡し方・読み方をする。
/// 万一 IME の中で落ちてもアプリ本体を巻き込まないよう、画面版はこれを別のプロセス（<see cref="ImeWorker"/>）で使う
/// </summary>
public sealed class ImeDictionary : IDisposable
{
    [ComImport, Guid("019F7153-E6DB-11D0-83C3-00C04FDDB82E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFEDictionary
    {
        // メソッドの並びは vtable の順そのもの。使わないものも順番を保つために宣言する
        [PreserveSig] int Open(IntPtr pchDictPath, IntPtr pshf);
        [PreserveSig] int Close();
        [PreserveSig] int GetHeader(IntPtr pchDictPath, IntPtr pshf, IntPtr pjfmt, IntPtr pulType);
        [PreserveSig] int DisplayProperty(IntPtr hwnd);
        [PreserveSig] int GetPosTable(out IntPtr prgPosTbl, out int pcPosTbl);
        [PreserveSig] int GetWords(IntPtr pwchFirst, IntPtr pwchLast, IntPtr pwchDisplay, uint ulPos, uint ulSelect,
            uint ulWordSrc, IntPtr pchBuffer, uint cbBuffer, out uint pcWrd);
        [PreserveSig] int NextWords(IntPtr pchBuffer, uint cbBuffer, out uint pcWrd);
        [PreserveSig] int Create(IntPtr pchDictPath, IntPtr pshf);
        [PreserveSig] int SetHeader(IntPtr pshf);
        [PreserveSig] int ExistWord(IntPtr pwrd);
        [PreserveSig] int ExistDependency(IntPtr pdp);
        [PreserveSig] int RegisterWord(int reg, IntPtr pwrd);
    }

    // IMEWRD: pwchReading, pwchDisplay, (nPos1, nPos2), rgulAttrs[2], cbComment, uct, pvComment。
    // msime.h は #pragma pack(1) だが、IME の中がそれと同じとは限らない。先頭から uct までの位置は詰めても詰めなくても同じなので、
    // それより後ろ（pvComment）は 0 のまま、どちらの大きさでも足りる領域を渡す
    static int OffsetDisplay => IntPtr.Size;
    static int OffsetPos => IntPtr.Size * 2;

    /// <summary>詰めて並べたときの IMEWRD の大きさ（x64 で 44）</summary>
    internal static int PackedWordSize => IntPtr.Size * 3 + 20;

    /// <summary>詰めずに並べたときの IMEWRD の大きさ（x64 で 48）</summary>
    internal static int NaturalWordSize => (PackedWordSize + IntPtr.Size - 1) / IntPtr.Size * IntPtr.Size;

    const int WordBufferSize = 64;
    const int IFED_REG_HEAD = 0;
    const int IFED_REG_DEL = 2;
    const int IMESHF_SIZE = 2 + 2 + 48 + 256 + 128;
    const int IFED_S_WORD_EXISTS = 0x00047202;   // MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_ITF, 0x7202)
    const int S_OK = 0;

    /// <summary>品詞名 → msime.h の JPOS 番号（IME の品詞表は読まない。並びが確かめられず、読むと落ちることがあったため）</summary>
    public static readonly IReadOnlyDictionary<string, ushort> PosNumbers = new Dictionary<string, ushort>
    {
        ["名詞"] = 100,       // JPOS_MEISHI_FUTSU
        ["人名"] = 106,       // JPOS_JINMEI
        ["地名"] = 109,       // JPOS_CHIMEI
        ["固有名詞"] = 118,   // JPOS_SONOTA（その他の固有名詞）
        ["短縮よみ"] = 913,   // JPOS_TANSHUKU（定型文などを短いよみで呼び出す）
    };

    readonly IFEDictionary dic;

    ImeDictionary(IFEDictionary dic) => this.dic = dic;

    /// <summary>
    /// IME のユーザー辞書を使っている最中に置く印のファイル。IME の中でアプリが落ちると消えずに残るので、
    /// 次の起動で「前回ここで落ちた」と分かる（落ちる種類の例外はアプリの中で受け止められないため）
    /// </summary>
    public static string? CrashMarkerPath { get; set; }

    /// <summary>前回、IME のユーザー辞書を使っている最中に落ちていたら true（印は消す）</summary>
    public static bool CrashedLastTime()
    {
        if (CrashMarkerPath == null || !File.Exists(CrashMarkerPath)) return false;
        try { File.Delete(CrashMarkerPath); } catch (Exception) { }
        return true;
    }

    static void SetMarker(bool on)
    {
        if (CrashMarkerPath == null) return;
        try
        {
            if (on) File.WriteAllText(CrashMarkerPath, DateTime.Now.ToString("O"));
            else File.Delete(CrashMarkerPath);
        }
        catch (Exception) { }
    }

    /// <summary>ユーザー辞書を開く。使えなければ例外（メッセージは利用者向け）</summary>
    public static ImeDictionary Open()
    {
        SetMarker(true);
        try
        {
            return OpenCore();
        }
        catch
        {
            SetMarker(false);
            throw;
        }
    }

    static ImeDictionary OpenCore()
    {
        var type = Type.GetTypeFromProgID("MSIME.Japan")
                   ?? throw new InvalidOperationException("Microsoft IME（MSIME.Japan）が見つかりません。");
        object obj;
        try
        {
            obj = Activator.CreateInstance(type)!;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"IME に接続できませんでした（{e.Message}）。", e);
        }
        if (obj is not IFEDictionary dic)
        {
            Marshal.ReleaseComObject(obj);
            throw new InvalidOperationException("この IME はユーザー辞書への直接登録（IFEDictionary）に対応していません。");
        }

        var shf = Marshal.AllocCoTaskMem(IMESHF_SIZE);
        try
        {
            for (int i = 0; i < IMESHF_SIZE; i++) Marshal.WriteByte(shf, i, 0);
            Marshal.WriteInt16(shf, IMESHF_SIZE);
            int hr = dic.Open(IntPtr.Zero, shf);   // パス null = 既定のユーザー辞書
            if (hr < 0)
            {
                Marshal.ReleaseComObject(dic);
                throw new InvalidOperationException($"ユーザー辞書を開けませんでした（0x{hr:X8}）。");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(shf);
        }
        return new ImeDictionary(dic);
    }

    /// <summary>0 で埋めた IMEWRD の領域を作り、よみ・表記・品詞だけを書く</summary>
    static IntPtr NewWord(IntPtr reading, IntPtr display, ushort pos)
    {
        var w = Marshal.AllocCoTaskMem(WordBufferSize);
        for (int i = 0; i < WordBufferSize; i++) Marshal.WriteByte(w, i, 0);
        Marshal.WriteIntPtr(w, 0, reading);
        Marshal.WriteIntPtr(w, OffsetDisplay, display);
        Marshal.WriteInt16(w, OffsetPos, (short)pos);
        return w;
    }

    RegisterResult Call(string reading, string word, string posName, bool delete)
    {
        if (!PosNumbers.TryGetValue(posName, out var nPos)) nPos = PosNumbers["名詞"];
        var r = Marshal.StringToCoTaskMemUni(reading);
        var d = Marshal.StringToCoTaskMemUni(word);
        var w = NewWord(r, d, nPos);
        try
        {
            if (!delete && dic.ExistWord(w) == S_OK) return RegisterResult.Exists;
            int hr = dic.RegisterWord(delete ? IFED_REG_DEL : IFED_REG_HEAD, w);
            if (hr == IFED_S_WORD_EXISTS) return RegisterResult.Exists;
            return hr >= 0 ? RegisterResult.Registered : RegisterResult.Failed;
        }
        catch (Exception)
        {
            return RegisterResult.Failed;
        }
        finally
        {
            Marshal.FreeCoTaskMem(w);
            Marshal.FreeCoTaskMem(r);
            Marshal.FreeCoTaskMem(d);
        }
    }

    const uint IFED_POS_ALL = 0x0001ffff;
    const uint IFED_SELECT_READING = 0x1, IFED_SELECT_DISPLAY = 0x2, IFED_SELECT_POS = 0x4;
    const uint IFED_REG_USER = 0x1;
    const int IFED_S_MORE_ENTRIES = 0x00047200;

    /// <summary>最後の GetWords の結果コード（環境チェックで、空なのか読めなかったのかを見分けるため）</summary>
    public int LastGetWordsResult { get; private set; }

    /// <summary>
    /// ユーザー辞書に入っている語（ユーザー登録分）をすべて読む。YuWords 以外の方法で入れた語も含む。
    /// 返ってきた領域は、文字の位置がその領域の中にあるかを確かめてから読む（並びの見込み違いで落ちないように）
    /// </summary>
    public List<(string Reading, string Word, string Pos)> GetUserWords()
    {
        var names = PosNumbers.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);
        var list = new List<(string, string, string)>();
        const int size = 1 << 20;
        var buf = Marshal.AllocCoTaskMem(size);
        try
        {
            int hr = dic.GetWords(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IFED_POS_ALL,
                IFED_SELECT_READING | IFED_SELECT_DISPLAY | IFED_SELECT_POS, IFED_REG_USER, buf, size, out uint n);
            LastGetWordsResult = hr;
            for (int guard = 0; hr >= 0 && guard < 10000; guard++)
            {
                list.AddRange(ParseWords(buf, size, (int)Math.Min(n, (uint)(size / PackedWordSize)), names));
                if (hr != IFED_S_MORE_ENTRIES) break;
                hr = dic.NextWords(buf, size, out n);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(buf);
        }
        return list;
    }

    /// <summary>GetWords の領域から語を取り出す。1件の大きさ（詰める・詰めない）は、文字の位置が領域の中に収まる方を選ぶ</summary>
    internal static List<(string Reading, string Word, string Pos)> ParseWords(IntPtr buf, int size, int n,
        IReadOnlyDictionary<ushort, string> names)
    {
        var result = new List<(string, string, string)>();
        if (n <= 0) return result;
        long start = buf.ToInt64(), end = start + size;
        bool Inside(IntPtr p) => p.ToInt64() >= start && p.ToInt64() < end;

        int? stride = null;
        foreach (var s in new[] { NaturalWordSize, PackedWordSize }.Distinct())
        {
            if ((long)s * n > size) continue;
            bool ok = true;
            for (int i = 0; i < n && ok; i++)
            {
                var e = buf + i * s;
                var r = Marshal.ReadIntPtr(e, 0);
                var d = Marshal.ReadIntPtr(e, OffsetDisplay);
                ok = Inside(r) && Inside(d) && r.ToInt64() >= start + (long)s * n;
            }
            if (ok) { stride = s; break; }
        }
        if (stride is not { } st) return result;   // どちらの並びでも辻褄が合わなければ読まない

        string? ReadString(IntPtr p)
        {
            var sb = new System.Text.StringBuilder();
            for (long a = p.ToInt64(); a + 1 < end && sb.Length < 200; a += 2)
            {
                var ch = (char)Marshal.ReadInt16(new IntPtr(a));
                if (ch == '\0') return sb.ToString();
                sb.Append(ch);
            }
            return null;   // 領域の中で終わらない
        }

        for (int i = 0; i < n; i++)
        {
            var e = buf + i * st;
            var reading = ReadString(Marshal.ReadIntPtr(e, 0));
            var display = ReadString(Marshal.ReadIntPtr(e, OffsetDisplay));
            var pos = (ushort)Marshal.ReadInt16(e, OffsetPos);
            if (!string.IsNullOrEmpty(reading) && !string.IsNullOrEmpty(display))
                result.Add((reading, display, names.GetValueOrDefault(pos, "名詞")));
        }
        return result;
    }

    /// <summary>1語を登録する。同じ語・よみ・品詞が既にあれば Exists</summary>
    public RegisterResult Register(string reading, string word, string posName) =>
        Call(reading, word, posName, delete: false);

    /// <summary>1語をユーザー辞書から消す</summary>
    public bool Delete(string reading, string word, string posName) =>
        Call(reading, word, posName, delete: true) == RegisterResult.Registered;

    public void Dispose()
    {
        try { dic.Close(); } catch (Exception) { }
        Marshal.ReleaseComObject(dic);
        SetMarker(false);
    }

    /// <summary>Microsoft IME のユーザー辞書ツールの場所。見つからなければ null</summary>
    public static string? DictionaryToolPath()
    {
        var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var path = Path.Combine(sys, "IME", "IMEJP", "IMJPDCT.EXE");
        return File.Exists(path) ? path : null;
    }
}
