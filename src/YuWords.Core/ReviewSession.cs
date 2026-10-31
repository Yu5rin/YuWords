using System.Text.Encodings.Web;
using System.Text.Json;

namespace YuWords.Core;

/// <summary>
/// 確認画面の途中の状態（session.json）。数百語の確認は1回で終わらないので、閉じても続きから再開できるようにする。
/// 例文（本文の1行）を含むため、設定フォルダ（%APPDATA%\YuWords）の外には置かない
/// </summary>
public sealed class ReviewSession
{
    public sealed record Entry(Candidate Candidate, ReviewItem.State State);

    public DateTime SavedAt { get; set; }
    /// <summary>読み込んだ取り込み元の名前（表示用）</summary>
    public List<string> Sources { get; set; } = [];
    public string CountHeader { get; set; } = "出現メール数";
    public string SignatureHeader { get; set; } = "署名";
    public List<Entry> Items { get; set; } = [];

    static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string PathIn(string dir) => System.IO.Path.Combine(dir, "session.json");

    public static ReviewSession From(IEnumerable<ReviewItem> items) =>
        new() { SavedAt = DateTime.Now, Items = items.Select(i => new Entry(i.Source, i.Save())).ToList() };

    /// <summary>一覧に戻す。登録済みかどうかは今の登録済みリストで判定し直す</summary>
    public List<ReviewItem> ToItems(Func<string, string, bool> isRegistered) =>
        Items.Select(e =>
        {
            var item = new ReviewItem(e.Candidate, false);
            item.Restore(e.State);
            item.IsRegistered = item.ComputeRegistered(isRegistered);
            return item;
        }).ToList();

    public void Save(string dir)
    {
        var path = PathIn(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, true);
    }

    /// <summary>保存された状態を読む。無い・壊れているときは null</summary>
    public static ReviewSession? Load(string dir)
    {
        var path = PathIn(dir);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<ReviewSession>(File.ReadAllText(path), Json); }
        catch (JsonException) { return null; }
    }

    public static void Delete(string dir)
    {
        var path = PathIn(dir);
        if (File.Exists(path)) File.Delete(path);
    }
}
