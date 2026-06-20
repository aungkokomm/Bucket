using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Bucket.Services;

/// <summary>
/// Turns a dropped/pasted <see cref="DataPackageView"/> into staged file paths.
/// Files pass through as-is; text, images, and links are materialized into a temp
/// staging folder so the rest of the app (copy/move/drag-out) treats them uniformly.
/// </summary>
public static class ContentStager
{
    private static string StageDir
    {
        get
        {
            string dir = Path.Combine(Storage.BaseDir, "staged");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static async Task<IReadOnlyList<string>> StageAsync(DataPackageView view)
    {
        var paths = new List<string>();
        try
        {
            // 1. Real files/folders — if present, that's the intent; take only those.
            if (view.Contains(StandardDataFormats.StorageItems))
            {
                IReadOnlyList<IStorageItem> items = await view.GetStorageItemsAsync();
                paths.AddRange(items.Where(i => !string.IsNullOrEmpty(i.Path)).Select(i => i.Path));
                if (paths.Count > 0)
                    return paths;
            }

            // 2. Image (browser image, screenshot, clipboard bitmap) → .png
            if (view.Contains(StandardDataFormats.Bitmap))
            {
                RandomAccessStreamReference bmpRef = await view.GetBitmapAsync();
                using var input = await bmpRef.OpenReadAsync();
                string path = NewPath("image", ".png");
                using (FileStream fs = File.Create(path))
                    await input.AsStreamForRead().CopyToAsync(fs);
                paths.Add(path);
                return paths;
            }

            // 3. Web link / URI → Windows internet shortcut (.url)
            Uri? uri = null;
            if (view.Contains(StandardDataFormats.WebLink))
                uri = await view.GetWebLinkAsync();
            else if (view.Contains(StandardDataFormats.ApplicationLink))
                uri = await view.GetApplicationLinkAsync();
            if (uri is not null)
            {
                string name = Sanitize(uri.Host is { Length: > 0 } h ? h : "link");
                string path = NewPath(name, ".url");
                File.WriteAllText(path, $"[InternetShortcut]\r\nURL={uri}\r\n", Encoding.UTF8);
                paths.Add(path);
                return paths;
            }

            // 4. Plain text → .txt snippet
            if (view.Contains(StandardDataFormats.Text))
            {
                string text = await view.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    string path = NewPath("snippet", ".txt");
                    File.WriteAllText(path, text, Encoding.UTF8);
                    paths.Add(path);
                }
            }
        }
        catch
        {
            // Unreadable/locked clipboard payload — return whatever we managed to stage.
        }
        return paths;
    }

    private static string NewPath(string baseName, string ext)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        return Path.Combine(StageDir, $"{baseName}_{stamp}{ext}");
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '-');
        return name.Length > 40 ? name[..40] : name;
    }
}
