using System.IO.Compression;
using Bucket.Models;

namespace Bucket.Services;

/// <summary>How to handle a destination path that already exists.</summary>
public enum ConflictAction
{
    Overwrite,
    Skip,
    KeepBoth,
    Cancel
}

/// <summary>Progress tick reported during a copy/move.</summary>
public sealed record OperationProgress(string CurrentItem, int Completed, int Total);

/// <summary>Outcome summary shown to the user after a copy/move.</summary>
public sealed class OperationResult
{
    public int Succeeded { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public bool Canceled { get; set; }
    public List<string> Errors { get; } = new();
    public bool Move { get; set; }

    public string Summary()
    {
        var verb = Move ? "Moved" : "Copied";
        var parts = new List<string> { $"{verb} {Format(Succeeded)}" };
        if (Skipped > 0) parts.Add($"skipped {Skipped}");
        if (Failed > 0) parts.Add($"failed {Failed}");
        if (Canceled) parts.Add("canceled");
        return string.Join(", ", parts) + ".";

        static string Format(int n) => $"{n} item{(n == 1 ? "" : "s")}";
    }
}

/// <summary>
/// Performs the only filesystem mutations in the app: copying or moving staged
/// items to a destination folder. Everything runs on a background thread; progress
/// is reported through <see cref="IProgress{T}"/> and the whole run is cancelable.
/// </summary>
public static class FileOperationsService
{
    public static Task<OperationResult> RunAsync(
        IReadOnlyList<FileItem> items,
        string destinationDir,
        bool move,
        ConflictAction conflictAction,
        IProgress<OperationProgress> progress,
        CancellationToken token)
    {
        return Task.Run(() =>
        {
            var result = new OperationResult { Move = move };
            int total = items.Sum(CountUnits);
            int done = 0;

            foreach (FileItem item in items)
            {
                token.ThrowIfCancellationRequested();

                string source = item.FullPath;
                string destPath = Path.Combine(destinationDir, item.Name);

                try
                {
                    // Guard against copying an item into itself or onto its own path.
                    if (PathsEqual(source, destPath))
                    {
                        result.Skipped++;
                        done += CountUnits(item);
                        progress.Report(new OperationProgress(item.Name, done, total));
                        continue;
                    }

                    bool exists = item.IsFolder ? Directory.Exists(destPath) : File.Exists(destPath);
                    if (exists)
                    {
                        switch (conflictAction)
                        {
                            case ConflictAction.Skip:
                                result.Skipped++;
                                done += CountUnits(item);
                                progress.Report(new OperationProgress(item.Name, done, total));
                                continue;
                            case ConflictAction.KeepBoth:
                                destPath = UniquePath(destPath, item.IsFolder);
                                break;
                            case ConflictAction.Overwrite:
                                DeleteExisting(destPath, item.IsFolder);
                                break;
                            case ConflictAction.Cancel:
                                result.Canceled = true;
                                return result;
                        }
                    }

                    if (item.IsFolder)
                        TransferDirectory(source, destPath, move, item.Name, progress, ref done, total, token);
                    else
                        TransferFile(source, destPath, move, item.Name, progress, ref done, total, token);

                    result.Succeeded++;
                }
                catch (OperationCanceledException)
                {
                    result.Canceled = true;
                    return result;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"{item.Name}: {ex.Message}");
                    done += CountUnits(item);
                    progress.Report(new OperationProgress(item.Name, done, total));
                }
            }

            return result;
        }, token);
    }

    // --- transfers -------------------------------------------------------

    private static void TransferFile(string source, string dest, bool move, string label,
        IProgress<OperationProgress> progress, ref int done, int total, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (move && SameVolume(source, dest))
        {
            File.Move(source, dest);
        }
        else
        {
            File.Copy(source, dest, overwrite: true);
            if (move)
                File.Delete(source);
        }
        done++;
        progress.Report(new OperationProgress(label, done, total));
    }

    private static void TransferDirectory(string source, string dest, bool move, string label,
        IProgress<OperationProgress> progress, ref int done, int total, CancellationToken token)
    {
        // A whole-folder move within the same volume is atomic and instant.
        if (move && SameVolume(source, dest) && !Directory.Exists(dest))
        {
            int units = Math.Max(1, CountFiles(source)); // count before the move removes source
            Directory.Move(source, dest);
            done = Math.Min(total, done + units);
            progress.Report(new OperationProgress(label, done, total));
            return;
        }

        CopyTree(source, dest, label, progress, ref done, total, token);
        if (move)
            Directory.Delete(source, recursive: true);
    }

    private static void CopyTree(string source, string dest, string label,
        IProgress<OperationProgress> progress, ref int done, int total, CancellationToken token)
    {
        Directory.CreateDirectory(dest);

        foreach (string file in Directory.EnumerateFiles(source))
        {
            token.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
            done++;
            progress.Report(new OperationProgress($"{label}\\{Path.GetFileName(file)}", done, total));
        }

        foreach (string dir in Directory.EnumerateDirectories(source))
        {
            token.ThrowIfCancellationRequested();
            CopyTree(dir, Path.Combine(dest, Path.GetFileName(dir)), label, progress, ref done, total, token);
        }
    }

    // --- helpers ---------------------------------------------------------

    /// <summary>Number of progress units an item represents (files copied).</summary>
    private static int CountUnits(FileItem item)
        => item.IsFolder ? Math.Max(1, CountFiles(item.FullPath)) : 1;

    private static int CountFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count(); }
        catch { return 1; }
    }

    private static void DeleteExisting(string path, bool isFolder)
    {
        if (isFolder) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }

    /// <summary>Generates "name (2).ext" / "name (2)" until the path is free.</summary>
    private static string UniquePath(string path, bool isFolder)
    {
        string dir = Path.GetDirectoryName(path)!;
        string name = isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        string ext = isFolder ? string.Empty : Path.GetExtension(path);

        int n = 2;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            n++;
        }
        while (isFolder ? Directory.Exists(candidate) : File.Exists(candidate));
        return candidate;
    }

    private static bool SameVolume(string a, string b)
        => string.Equals(Path.GetPathRoot(Path.GetFullPath(a)),
                          Path.GetPathRoot(Path.GetFullPath(b)),
                          StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\'),
                          Path.GetFullPath(b).TrimEnd('\\'),
                          StringComparison.OrdinalIgnoreCase);

    // --- transform-on-export --------------------------------------------

    /// <summary>Packs every staged item into a single .zip in the destination folder.</summary>
    public static Task<OperationResult> ZipAsync(
        IReadOnlyList<FileItem> items, string destinationDir,
        IProgress<OperationProgress> progress, CancellationToken token)
    {
        return Task.Run(() =>
        {
            var result = new OperationResult();
            int total = items.Sum(CountUnits);
            int done = 0;
            string zipPath = UniquePath(
                Path.Combine(destinationDir, $"Bucket_{DateTime.Now:yyyyMMdd-HHmmss}.zip"), false);
            try
            {
                using var zip = System.IO.Compression.ZipFile.Open(
                    zipPath, System.IO.Compression.ZipArchiveMode.Create);
                foreach (FileItem item in items)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (item.IsFolder)
                        {
                            foreach (string file in Directory.EnumerateFiles(item.FullPath, "*", SearchOption.AllDirectories))
                            {
                                token.ThrowIfCancellationRequested();
                                string rel = Path.Combine(item.Name, Path.GetRelativePath(item.FullPath, file));
                                zip.CreateEntryFromFile(file, rel);
                                progress.Report(new OperationProgress(rel, ++done, total));
                            }
                        }
                        else
                        {
                            zip.CreateEntryFromFile(item.FullPath, item.Name);
                            progress.Report(new OperationProgress(item.Name, ++done, total));
                        }
                        result.Succeeded++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { result.Failed++; result.Errors.Add($"{item.Name}: {ex.Message}"); }
                }
            }
            catch (OperationCanceledException)
            {
                result.Canceled = true;
                try { File.Delete(zipPath); } catch { }
            }
            return result;
        }, token);
    }

    /// <summary>
    /// Copies every file from the staged items into one flat destination folder
    /// (subfolders are flattened). When <paramref name="renamePrefix"/> is set, files
    /// are renamed sequentially (prefix001.ext, prefix002.ext, …).
    /// </summary>
    public static Task<OperationResult> CopyFlatAsync(
        IReadOnlyList<FileItem> items, string destinationDir, string? renamePrefix,
        ConflictAction conflictAction, IProgress<OperationProgress> progress, CancellationToken token)
    {
        return Task.Run(() =>
        {
            var result = new OperationResult();

            // Gather every file (flattening folders).
            var files = new List<string>();
            foreach (FileItem item in items)
            {
                if (item.IsFolder)
                {
                    try { files.AddRange(Directory.EnumerateFiles(item.FullPath, "*", SearchOption.AllDirectories)); }
                    catch { }
                }
                else if (File.Exists(item.FullPath))
                {
                    files.Add(item.FullPath);
                }
            }

            int total = files.Count;
            int done = 0;
            int seq = 1;
            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    string name = renamePrefix is { Length: > 0 }
                        ? $"{renamePrefix}{seq:000}{Path.GetExtension(file)}"
                        : Path.GetFileName(file);
                    seq++;

                    string dest = Path.Combine(destinationDir, name);
                    if (File.Exists(dest) && !PathsEqual(file, dest))
                    {
                        switch (conflictAction)
                        {
                            case ConflictAction.Skip: result.Skipped++; progress.Report(new OperationProgress(name, ++done, total)); continue;
                            case ConflictAction.KeepBoth: dest = UniquePath(dest, false); break;
                            case ConflictAction.Cancel: result.Canceled = true; return result;
                        }
                    }
                    File.Copy(file, dest, overwrite: conflictAction == ConflictAction.Overwrite);
                    result.Succeeded++;
                    progress.Report(new OperationProgress(name, ++done, total));
                }
                catch (OperationCanceledException) { result.Canceled = true; return result; }
                catch (Exception ex) { result.Failed++; result.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
            }
            return result;
        }, token);
    }
}
