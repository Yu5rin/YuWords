using System.Runtime.InteropServices;
using System.Text;

namespace YuWords.Core;

/// <summary>1語の登録結果</summary>
public enum RegisterResult { Registered, Exists, Failed }

/// <summary>
/// Microsoft IME のユーザー辞書（IFEDictionary）への直接登録。
/// 定義は Windows SDK の msime.h に合わせている。新しい IME で使えない場合は Open が失敗する
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
        [PreserveSig] int ExistWord(ref IMEWRD pwrd);
        [PreserveSig] int ExistDependency(IntPtr pdp);
        [PreserveSig] int RegisterWord(int reg, ref IMEWRD pwrd);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IMEWRD
    {
        public IntPtr pwchReading;
        public IntPtr pwchDisplay;
        public ushort nPos1;
        public ushort nPos2;
        public uint rgulAttrs0;
        public uint rgulAttrs1;
        public int cbComment;
        public int uct;
        public IntPtr pvComment;
    }

    /// <summary>IMEWRD の大きさ（msime.h と同じ並びになっているかのテスト用。x64 で 48）</summary>
    internal static int WordStructSize => Marshal.SizeOf<IMEWRD>();

    /// <summary>実際に使う品詞番号（実機確認用）</summary>
    public IReadOnlyDictionary<string, ushort> PosNumbers => pos;

    const int IFED_REG_HEAD = 0;
    const int IFED_REG_DEL = 2;
    const int IMESHF_SIZE = 2 + 2 + 48 + 256 + 128;
    const int IFED_S_WORD_EXISTS = 0x00047202;   // MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_ITF, 0x7202)
    const int S_OK = 0;

    /// <summary>品詞名 → msime.h の JPOS 番号（実際の表が取れればそちらを優先する）</summary>
    static readonly Dictionary<string, ushort> DefaultPos = new()
    {
        ["名詞"] = 100,       // JPOS_MEISHI_FUTSU
        ["人名"] = 106,       // JPOS_JINMEI
        ["地名"] = 109,       // JPOS_CHIMEI
        ["固有名詞"] = 118,   // JPOS_SONOTA（その他の固有名詞）
        ["短縮よみ"] = 913,   // JPOS_TANSHUKU（定型文などを短いよみで呼び出す）
    };

    readonly IFEDictionary dic;
    readonly Dictionary<string, ushort> pos;

    ImeDictionary(IFEDictionary dic, Dictionary<string, ushort> pos)
    {
        this.dic = dic;
        this.pos = pos;
    }

    /// <summary>ユーザー辞書を開く。使えなければ例外（メッセージは利用者向け）</summary>
    public static ImeDictionary Open()
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
        return new ImeDictionary(dic, ReadPosTable(dic));
    }

    /// <summary>IME の品詞表（名前は Shift_JIS）から、使う品詞の番号を引く</summary>
    static Dictionary<string, ushort> ReadPosTable(IFEDictionary dic)
    {
        var map = new Dictionary<string, ushort>(DefaultPos);
        try
        {
            if (dic.GetPosTable(out var tbl, out int n) < 0 || tbl == IntPtr.Zero) return map;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var sjis = Encoding.GetEncoding(932);
            int size = IntPtr.Size * 2;   // { WORD nPos; BYTE* szName; }（ポインタ境界に揃う）
            var found = new Dictionary<string, ushort>();
            for (int i = 0; i < n; i++)
            {
                var entry = tbl + i * size;
                var nPos = (ushort)Marshal.ReadInt16(entry);
                var namePtr = Marshal.ReadIntPtr(entry + IntPtr.Size);
                if (namePtr == IntPtr.Zero) continue;
                int len = 0;
                while (Marshal.ReadByte(namePtr + len) != 0 && len < 64) len++;
                var bytes = new byte[len];
                Marshal.Copy(namePtr, bytes, 0, len);
                found.TryAdd(sjis.GetString(bytes), nPos);
            }
            foreach (var name in DefaultPos.Keys)
                if (found.TryGetValue(name, out var p)) map[name] = p;
        }
        catch (Exception)
        {
            // 表が読めなければ msime.h の番号のまま
        }
        return map;
    }

    RegisterResult Call(string reading, string word, string posName, bool delete)
    {
        if (!pos.TryGetValue(posName, out var nPos)) nPos = DefaultPos["名詞"];
        var r = Marshal.StringToCoTaskMemUni(reading);
        var d = Marshal.StringToCoTaskMemUni(word);
        try
        {
            var w = new IMEWRD { pwchReading = r, pwchDisplay = d, nPos1 = nPos };
            if (!delete && dic.ExistWord(ref w) == S_OK) return RegisterResult.Exists;
            int hr = dic.RegisterWord(delete ? IFED_REG_DEL : IFED_REG_HEAD, ref w);
            if (hr == IFED_S_WORD_EXISTS) return RegisterResult.Exists;
            return hr >= 0 ? RegisterResult.Registered : RegisterResult.Failed;
        }
        catch (Exception)
        {
            return RegisterResult.Failed;
        }
        finally
        {
            Marshal.FreeCoTaskMem(r);
            Marshal.FreeCoTaskMem(d);
        }
    }

    const uint IFED_POS_ALL = 0x0001ffff;
    const uint IFED_SELECT_READING = 0x1, IFED_SELECT_DISPLAY = 0x2, IFED_SELECT_POS = 0x4;
    const uint IFED_REG_USER = 0x1;
    const int IFED_S_MORE_ENTRIES = 0x00047200;

    /// <summary>
    /// ユーザー辞書に入っている語（ユーザー登録分）をすべて読む。
    /// YuWords 以外の方法（ユーザー辞書ツール・単語の登録）で入れた語も含む
    /// </summary>
    public List<(string Reading, string Word, string Pos)> GetUserWords()
    {
        var names = pos.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);
        var list = new List<(string, string, string)>();
        const int size = 1 << 20;
        var buf = Marshal.AllocCoTaskMem(size);
        try
        {
            int hr = dic.GetWords(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IFED_POS_ALL,
                IFED_SELECT_READING | IFED_SELECT_DISPLAY | IFED_SELECT_POS, IFED_REG_USER, buf, size, out uint n);
            for (int guard = 0; hr >= 0 && guard < 10000; guard++)
            {
                for (int i = 0; i < n; i++)
                {
                    var w = Marshal.PtrToStructure<IMEWRD>(buf + i * Marshal.SizeOf<IMEWRD>());
                    var reading = Marshal.PtrToStringUni(w.pwchReading) ?? "";
                    var display = Marshal.PtrToStringUni(w.pwchDisplay) ?? "";
                    if (reading.Length > 0 && display.Length > 0)
                        list.Add((reading, display, names.GetValueOrDefault(w.nPos1, "名詞")));
                }
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
    }

    /// <summary>Microsoft IME のユーザー辞書ツールの場所。見つからなければ null</summary>
    public static string? DictionaryToolPath()
    {
        var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var path = Path.Combine(sys, "IME", "IMEJP", "IMJPDCT.EXE");
        return File.Exists(path) ? path : null;
    }
}
