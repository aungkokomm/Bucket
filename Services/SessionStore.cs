using System.Text.Json;
using Bucket.Models;

namespace Bucket.Services;

/// <summary>Serializable snapshot of a single bucket window.</summary>
public sealed class BucketState
{
    public List<string> Paths { get; set; } = new();
    public string Name { get; set; } = "";
    public BucketColor Color { get; set; } = BucketColor.Blue;
    public ViewMode View { get; set; } = ViewMode.CompactList;
    public WindowMode Mode { get; set; } = WindowMode.Mid;
    public bool AlwaysOnTop { get; set; } = true;
}

/// <summary>
/// Persists the reference lists of buckets that were closed with items still in
/// them, so they can be re-offered next launch. Stores paths only — never copies,
/// moves, or reads file contents. This is the single, deliberate exception to the
/// app's otherwise stateless design.
/// </summary>
public static class SessionStore
{
    private static readonly string FilePath = Storage.PathTo("session.json");

    public static bool HasSession => File.Exists(FilePath);

    public static void Save(IEnumerable<BucketState> buckets)
    {
        try
        {
            var list = buckets.Where(b => b.Paths.Count > 0).ToList();
            if (list.Count == 0)
            {
                Clear();
                return;
            }
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list));
        }
        catch { /* best-effort */ }
    }

    public static List<BucketState> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<BucketState>>(File.ReadAllText(FilePath));
                if (list is not null)
                {
                    // Drop paths that no longer exist so we never resurrect dead references.
                    foreach (var b in list)
                        b.Paths = b.Paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
                    return list.Where(b => b.Paths.Count > 0).ToList();
                }
            }
        }
        catch { /* corrupt file — ignore */ }
        return new List<BucketState>();
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch { /* ignore */ }
    }
}
