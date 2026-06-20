using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Bucket.Models;

/// <summary>
/// A single staged file or folder. Holds metadata and a path reference only —
/// constructing a <see cref="FileItem"/> never copies, moves, or modifies the
/// underlying file. The bucket is a list of these references until the user
/// explicitly chooses Copy or Move.
/// </summary>
public partial class FileItem : ObservableObject
{
    public string FullPath { get; }
    public string Name { get; private set; } = string.Empty;
    public string Extension { get; private set; } = string.Empty;
    public bool IsFolder { get; private set; }
    public long Size { get; private set; }
    public DateTime DateModified { get; private set; }

    /// <summary>Friendly type name (e.g. "Text Document", "Folder"). Filled lazily.</summary>
    [ObservableProperty]
    public partial string TypeDescription { get; set; } = string.Empty;

    /// <summary>Shell thumbnail / type icon. Loaded asynchronously and lazily.</summary>
    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    private bool _shellInfoLoaded;

    public FileItem(string fullPath)
    {
        FullPath = fullPath;
        LoadMetadata();
    }

    /// <summary>
    /// Reads cheap metadata synchronously from the file system. Folder sizes are
    /// not computed here (recursive enumeration is expensive and would stall the
    /// UI on add) — folders report a size of -1, shown as "—".
    /// </summary>
    public void LoadMetadata()
    {
        IsFolder = Directory.Exists(FullPath);
        if (IsFolder)
        {
            var di = new DirectoryInfo(FullPath);
            Name = di.Name.Length > 0 ? di.Name : FullPath; // root drives have empty Name
            Extension = string.Empty;
            Size = -1;
            DateModified = SafeTime(() => di.LastWriteTime);
            TypeDescription = "Folder";
        }
        else
        {
            var fi = new FileInfo(FullPath);
            Name = fi.Name;
            Extension = fi.Extension;
            Size = SafeLong(() => fi.Length);
            DateModified = SafeTime(() => fi.LastWriteTime);
            TypeDescription = string.IsNullOrEmpty(Extension)
                ? "File"
                : Extension.TrimStart('.').ToUpperInvariant() + " File";
        }
    }

    /// <summary>
    /// Loads the friendly type description and a shell thumbnail/icon. Safe to call
    /// repeatedly — the work runs only once. <paramref name="size"/> is the requested
    /// edge length in pixels (larger for Gallery view).
    /// </summary>
    public async Task LoadThumbnailAsync(uint size = 64)
    {
        if (_shellInfoLoaded)
            return;
        _shellInfoLoaded = true;

        try
        {
            IStorageItem item = IsFolder
                ? await StorageFolder.GetFolderFromPathAsync(FullPath)
                : await StorageFile.GetFileFromPathAsync(FullPath);

            if (item is StorageFile file)
            {
                if (!string.IsNullOrWhiteSpace(file.DisplayType))
                    TypeDescription = file.DisplayType;

                using StorageItemThumbnail thumb =
                    await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.UseCurrentScale);
                await SetThumbnailAsync(thumb);
            }
            else if (item is StorageFolder folder)
            {
                using StorageItemThumbnail thumb =
                    await folder.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.UseCurrentScale);
                await SetThumbnailAsync(thumb);
            }
        }
        catch
        {
            // Inaccessible path, permission denied, or no thumbnail available —
            // the view falls back to a generic glyph. Allow a later retry.
            _shellInfoLoaded = false;
        }
    }

    private async Task SetThumbnailAsync(StorageItemThumbnail? thumb)
    {
        if (thumb is null || thumb.Size == 0)
            return;

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(thumb);
        Thumbnail = bitmap;
    }

    private static long SafeLong(Func<long> get)
    {
        try { return get(); } catch { return 0; }
    }

    private static DateTime SafeTime(Func<DateTime> get)
    {
        try { return get(); } catch { return DateTime.MinValue; }
    }
}
