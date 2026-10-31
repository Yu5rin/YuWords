using System.Text;

namespace YuWords.Core;

/// <summary>テキストファイルをメール群として読む（Outlook なしで試す用）</summary>
public static class MailTextSource
{
    /// <summary>「---」だけの行で区切ったテキストを、メール単位の行リスト（引用除去済み）にする</summary>
    public static List<List<string>> Read(string path)
    {
        var mails = new List<List<string>>();
        var current = new StringBuilder();
        foreach (var line in File.ReadAllLines(path).Append("---"))
        {
            if (line.Trim() == "---")
            {
                if (current.Length > 0) mails.Add(MailPreprocessor.StripQuote(current.ToString()));
                current.Clear();
            }
            else current.AppendLine(line);
        }
        return mails;
    }
}
