using System.Text.Json;

namespace Bucket.Services;

/// <summary>
/// Small app preferences, persisted to a JSON file alongside the other state.
/// Identity-independent so it works in both packaged and unpackaged builds.
/// Stores preferences only — never any user file content.
/// </summary>
public static class AppSettings
{
    private static readonly string FilePath = Storage.PathTo("settings.json");
    private static readonly Data Current = Load();

    private sealed class Data
    {
        public bool RestoreSessionEnabled { get; set; } = true;
        public int DefaultViewMode { get; set; } = 1; // CompactList
        public bool KeepRunningInTray { get; set; } = true;
        public int DefaultWindowMode { get; set; } = 1; // Mid
        public int WindowOpacity { get; set; } = 100;   // percent, 40–100
        public bool ShakeToSummon { get; set; } = true;
        public bool FirstRunDone { get; set; }
        public bool EdgeCatcher { get; set; } = false;
    }

    /// <summary>
    /// When true, a bucket closed with items in it is remembered and re-offered on
    /// next launch (path references only — files are never touched).
    /// </summary>
    public static bool RestoreSessionEnabled
    {
        get => Current.RestoreSessionEnabled;
        set { Current.RestoreSessionEnabled = value; Save(); }
    }

    /// <summary>Last view mode chosen, applied to newly created buckets.</summary>
    public static int DefaultViewMode
    {
        get => Current.DefaultViewMode;
        set { Current.DefaultViewMode = value; Save(); }
    }

    /// <summary>
    /// When true, closing the last bucket window leaves the app running in the
    /// system tray instead of exiting. Toggled from the tray menu.
    /// </summary>
    public static bool KeepRunningInTray
    {
        get => Current.KeepRunningInTray;
        set { Current.KeepRunningInTray = value; Save(); }
    }

    /// <summary>Last window mode used (Compact/Mid), applied to new buckets.</summary>
    public static int DefaultWindowMode
    {
        get => Current.DefaultWindowMode;
        set { Current.DefaultWindowMode = value; Save(); }
    }

    /// <summary>Window opacity in percent (40–100). Applied to all bucket windows.</summary>
    public static int WindowOpacity
    {
        get => Math.Clamp(Current.WindowOpacity, 40, 100);
        set { Current.WindowOpacity = Math.Clamp(value, 40, 100); Save(); }
    }

    /// <summary>Shake the mouse to summon a bucket under the cursor.</summary>
    public static bool ShakeToSummon
    {
        get => Current.ShakeToSummon;
        set { Current.ShakeToSummon = value; Save(); }
    }

    /// <summary>Whether the first-run tip has been shown.</summary>
    public static bool FirstRunDone
    {
        get => Current.FirstRunDone;
        set { Current.FirstRunDone = value; Save(); }
    }

    /// <summary>Show the screen-edge drop catcher.</summary>
    public static bool EdgeCatcher
    {
        get => Current.EdgeCatcher;
        set { Current.EdgeCatcher = value; Save(); }
    }

    private static Data Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
        }
        catch { /* corrupt or unreadable — use defaults */ }
        return new Data();
    }

    private static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Current)); }
        catch { /* best-effort */ }
    }
}
