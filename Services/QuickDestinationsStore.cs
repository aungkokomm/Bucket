using System.Collections.ObjectModel;
using System.Text.Json;

namespace Bucket.Services;

/// <summary>A pinned folder the user can copy/move to with one click.</summary>
public sealed class QuickDestination
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Shared, persisted list of pinned destination folders. Backed by a small JSON
/// file in the app's local data folder. A single shared collection is exposed so
/// every bucket window stays in sync.
/// </summary>
public static class QuickDestinationsStore
{
    private static readonly string FilePath = Storage.PathTo("quick_destinations.json");

    private static ObservableCollection<QuickDestination>? _items;

    public static ObservableCollection<QuickDestination> Items
    {
        get
        {
            if (_items is null)
            {
                _items = Load();
                _items.CollectionChanged += (_, _) => Save();
            }
            return _items;
        }
    }

    public static void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;
        if (Items.Any(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;

        Items.Add(new QuickDestination
        {
            Path = path,
            Name = new DirectoryInfo(path).Name is { Length: > 0 } n ? n : path
        });
    }

    public static void Remove(QuickDestination destination) => Items.Remove(destination);

    private static ObservableCollection<QuickDestination> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<QuickDestination>>(File.ReadAllText(FilePath));
                if (list is not null)
                    return new ObservableCollection<QuickDestination>(list.Where(d => Directory.Exists(d.Path)));
            }
        }
        catch { /* corrupt or unreadable file — start fresh */ }
        return new ObservableCollection<QuickDestination>();
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items));
        }
        catch { /* best-effort persistence */ }
    }
}
