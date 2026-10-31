namespace YuWords.Core;

/// <summary>抽出の設定。既定値は参照実装（Python のプロトタイプ）と同じ</summary>
public sealed class ExtractorSettings
{
    /// <summary>何日前までの送信メールを対象にするか</summary>
    public int Days { get; set; } = 180;

    /// <summary>読み込む最大件数</summary>
    public int MaxMails { get; set; } = 2000;

    /// <summary>何通以上に出た語を候補にするか</summary>
    public int MinMails { get; set; } = 3;

    /// <summary>この割合以上のメールに出る行を署名・定型文とみなす</summary>
    public double CommonLineRatio { get; set; } = 0.1;

    /// <summary>結合する複合語の最大文字数</summary>
    public int MaxCompoundLength { get; set; } = 15;

    /// <summary>署名・あいさつなど繰り返し出る行を、定型文（短縮よみ）の候補として出す数。0 なら出さない</summary>
    public int MaxPhrases { get; set; } = 30;

    /// <summary>よみのルール（「表記=よみ」の行）。複合語の一部に合うときもそのよみを使う（係=がかり、XYZ=みー など）</summary>
    public List<string> ReadingRules { get; set; } = [];

    /// <summary>ストップワードの追加分</summary>
    public List<string> ExtraStopWords { get; set; } = [];

    /// <summary>複合語の先頭から外す語の追加分</summary>
    public List<string> ExtraLeadStrip { get; set; } = [];
}
