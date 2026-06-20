using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Bucket.Services;

/// <summary>Reads file/folder paths from the Windows clipboard.</summary>
public static class ClipboardHelper
{
    /// <summary>
    /// Returns the paths of any files/folders currently on the clipboard, or an
    /// empty list if the clipboard holds no storage items.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetPathsAsync()
    {
        var paths = new List<string>();
        try
        {
            DataPackageView view = Clipboard.GetContent();
            if (!view.Contains(StandardDataFormats.StorageItems))
                return paths;

            IReadOnlyList<IStorageItem> items = await view.GetStorageItemsAsync();
            foreach (IStorageItem item in items)
            {
                if (!string.IsNullOrEmpty(item.Path))
                    paths.Add(item.Path);
            }
        }
        catch
        {
            // Clipboard can be locked by another process or hold an unreadable
            // payload — treat as "nothing to paste".
        }
        return paths;
    }
}
