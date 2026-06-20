using Bucket.Models;
using Microsoft.UI.Xaml;

namespace Bucket.ViewModels;

/// <summary>
/// The window-level services a <see cref="BucketViewModel"/> needs but that
/// belong to the view layer (pickers, window chrome, spawning windows). Keeps the
/// view model testable and free of direct WinUI window dependencies.
/// </summary>
public interface IBucketHost
{
    /// <summary>XamlRoot of the owning window, for anchoring dialogs.</summary>
    XamlRoot? XamlRoot { get; }

    /// <summary>Shows a multi-select file picker; returns chosen paths (possibly empty).</summary>
    Task<IReadOnlyList<string>> PickFilesAsync();

    /// <summary>Shows a folder picker for adding a folder to the bucket.</summary>
    Task<string?> PickFolderToAddAsync();

    /// <summary>Shows a folder picker for a copy/move destination or a pinned folder.</summary>
    Task<string?> PickDestinationAsync();

    /// <summary>Applies the always-on-top window state.</summary>
    void SetAlwaysOnTop(bool value);

    /// <summary>Swaps the item template/panel for the given list view mode.</summary>
    void ApplyViewMode(ViewMode mode);

    /// <summary>Resizes the window to the given size mode (compact/mid square).</summary>
    void ApplyWindowMode(WindowMode mode, bool animate);

    /// <summary>Creates a new bucket window cascaded from this one.</summary>
    void CreateBucketNearMe();
}
