using Windows.Storage;

namespace Bucket.Services;

/// <summary>
/// Resolves the folder used for the app's small state files (settings, pinned
/// destinations, session). Works whether the app runs packaged (MSIX, dev) or
/// unpackaged (the Inno Setup install), where <see cref="ApplicationData.Current"/>
/// is unavailable and throws.
/// </summary>
public static class Storage
{
    public static string BaseDir { get; } = Resolve();

    public static string PathTo(string fileName) => System.IO.Path.Combine(BaseDir, fileName);

    private static string Resolve()
    {
        try
        {
            // Packaged: use the per-user app data folder provided by the platform.
            return ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            // Unpackaged: no package identity — fall back to %LOCALAPPDATA%\Bucket.
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bucket");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
