using System.Runtime.InteropServices;

namespace YuWords.Core;

/// <summary>Microsoft IME の変換エンジン（IFELanguage）。よみ→変換結果を参考情報として取る</summary>
public sealed class ImeConverter : IDisposable
{
    // msime.h の IFELanguage。メソッドの並びは vtable の順そのもの
    [ComImport, Guid("019F7152-E6DB-11D0-83C3-00C04FDDB82E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFELanguage
    {
        [PreserveSig] int Open();
        [PreserveSig] int Close();
        [PreserveSig] int GetJMorphResult(uint dwRequest, uint dwCMode, int cwchInput,
            [MarshalAs(UnmanagedType.LPWStr)] string pwchInput, IntPtr pfCInfo, out IntPtr ppResult);
        [PreserveSig] int GetConversionModeCaps(out uint pdwCaps);
        [PreserveSig] int GetPhonetic([MarshalAs(UnmanagedType.BStr)] string str, int start, int length,
            [MarshalAs(UnmanagedType.BStr)] out string result);
        [PreserveSig] int GetConversion([MarshalAs(UnmanagedType.BStr)] string str, int start, int length,
            [MarshalAs(UnmanagedType.BStr)] out string result);
    }

    readonly IFELanguage? ife;

    /// <summary>接続できなかったときの理由。接続できたら null</summary>
    public string? Error { get; }

    public bool IsAvailable => ife != null;

    public ImeConverter()
    {
        try
        {
            var type = Type.GetTypeFromProgID("MSIME.Japan")
                       ?? throw new COMException("MSIME.Japan が登録されていません");
            var obj = (IFELanguage)Activator.CreateInstance(type)!;
            int hr = obj.Open();
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            ife = obj;
        }
        catch (Exception e)
        {
            Error = e.Message;
        }
    }

    /// <summary>よみを変換した結果。取れなければ空文字</summary>
    public string Convert(string reading)
    {
        if (ife == null || reading.Length == 0) return "";
        try
        {
            return ife.GetConversion(reading, 1, -1, out var result) >= 0 ? result ?? "" : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>表記を IME に渡したときのよみ（ひらがな）。取れなければ空文字</summary>
    public string Phonetic(string word)
    {
        if (ife == null || word.Length == 0) return "";
        try
        {
            return ife.GetPhonetic(word, 1, -1, out var result) >= 0 ? TermExtractor.KataToHira(result ?? "") : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    public void Dispose()
    {
        if (ife == null) return;
        ife.Close();
        Marshal.ReleaseComObject(ife);
    }
}
