using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Bucket.Helpers;
using Bucket.Models;
using Bucket.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;

namespace Bucket.ViewModels;

/// <summary>
/// State and behavior for a single bucket: the staged item list, view options,
/// and the copy/move/remove/undo operations. Holds path references only — adding
/// or removing an item never touches the file system.
/// </summary>
public partial class BucketViewModel : ObservableObject
{
    private readonly IBucketHost _host;

    /// <summary>The staged items. The single source of truth for a bucket.</summary>
    public ObservableCollection<FileItem> Items { get; } = new();

    /// <summary>Current selection, kept in sync by the view.</summary>
    public List<FileItem> Selection { get; } = new();

    private UndoState? _undo;

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial ViewMode CurrentView { get; set; }
    [ObservableProperty] public partial WindowMode Mode { get; set; }
    [ObservableProperty] public partial BucketColor Color { get; set; }
    [ObservableProperty] public partial SolidColorBrush AccentBrush { get; set; }
    [ObservableProperty] public partial SolidColorBrush BackgroundBrush { get; set; }
    [ObservableProperty] public partial Brush TitleBarBrush { get; set; }
    [ObservableProperty] public partial bool AlwaysOnTop { get; set; } = true;
    [ObservableProperty] public partial bool RestoredFromSession { get; set; }
    [ObservableProperty] public partial bool FirstRunTip { get; set; }
    [ObservableProperty] public partial string FilterText { get; set; } = "";

    // Busy / progress overlay
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string ProgressText { get; set; } = string.Empty;
    [ObservableProperty] public partial double ProgressValue { get; set; }
    [ObservableProperty] public partial double ProgressMax { get; set; } = 1;

    private CancellationTokenSource? _cts;

    public BucketViewModel(IBucketHost host, BucketState? state = null)
    {
        _host = host;
        Items.CollectionChanged += OnItemsChanged;

        // Apply restored or default state.
        Name = state?.Name ?? "";
        Color = state?.Color ?? BucketColor.Blue;
        AccentBrush = new SolidColorBrush(ColorPalette.ToColor(Color));
        BackgroundBrush = new SolidColorBrush(ColorPalette.ToBackground(Color));
        TitleBarBrush = BuildTitleBarBrush(Color);
        AlwaysOnTop = state?.AlwaysOnTop ?? true;
        CurrentView = state?.View ?? (ViewMode)AppSettings.DefaultViewMode;
        Mode = state?.Mode ?? (WindowMode)AppSettings.DefaultWindowMode;

        if (state is { Paths.Count: > 0 })
        {
            AddPaths(state.Paths);
            RestoredFromSession = true;
        }

        // One-time tip on the very first bucket the user ever opens.
        if (!AppSettings.FirstRunDone)
        {
            FirstRunTip = true;
            AppSettings.FirstRunDone = true;
        }
    }

    /// <summary>Shared pinned destinations (same list across all buckets).</summary>
    public ObservableCollection<QuickDestination> QuickDestinations => QuickDestinationsStore.Items;

    // --- derived display state ------------------------------------------

    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => Items.Count == 0;
    public string CountText => Format.Count(Items.Count, "item");

    // Window size mode helpers
    public bool IsMid => Mode == WindowMode.Mid;
    public bool IsCompact => Mode == WindowMode.Compact;
    public string ToggleModeGlyph => IsCompact ? "" : ""; // expand / collapse
    public string ToggleModeTip => IsCompact ? "Expand" : "Collapse";

    // Compact-mode big readout
    public string CountNumber => Items.Count.ToString();
    public string CountNoun => Items.Count == 1 ? "item" : "items";
    public string TotalSizeText
    {
        get
        {
            long total = Items.Where(i => !i.IsFolder && i.Size > 0).Sum(i => i.Size);
            bool hasFolders = Items.Any(i => i.IsFolder);
            return Format.Size(total) + (hasFolders ? " + folders" : string.Empty);
        }
    }

    public bool CanUndo => _undo is not null;
    public string UndoLabel => _undo?.Label ?? "Undo";

    // --- adding ----------------------------------------------------------

    /// <summary>
    /// Adds paths to the bucket, ignoring duplicates and non-existent paths.
    /// Kicks off async thumbnail/type loading for each new item.
    /// </summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            if (!File.Exists(path) && !Directory.Exists(path))
                continue;
            if (Items.Any(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var item = new FileItem(path);
            Items.Add(item);
            _ = item.LoadThumbnailAsync(96);
        }
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        IReadOnlyList<string> files = await _host.PickFilesAsync();
        if (files.Count > 0)
            AddPaths(files);
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        string? folder = await _host.PickFolderToAddAsync();
        if (folder is not null)
            AddPaths(new[] { folder });
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        try
        {
            IReadOnlyList<string> paths =
                await ContentStager.StageAsync(Windows.ApplicationModel.DataTransfer.Clipboard.GetContent());
            if (paths.Count > 0)
                AddPaths(paths);
        }
        catch { /* clipboard locked / unreadable */ }
    }

    // --- removing / undo -------------------------------------------------

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveSelected()
    {
        if (Selection.Count == 0)
            return;

        // Capture for undo (item + original index), highest index first so
        // re-insertion restores the original order.
        var removed = Selection
            .Select(i => (item: i, index: Items.IndexOf(i)))
            .Where(t => t.index >= 0)
            .OrderByDescending(t => t.index)
            .ToList();

        foreach (var (item, _) in removed)
            Items.Remove(item);

        Selection.Clear();
        SetUndo("Undo Remove", removed.OrderBy(t => t.index).ToList());
    }

    private bool HasSelection() => Selection.Count > 0;

    [RelayCommand(CanExecute = nameof(HasItems))]
    private async Task EmptyAsync()
    {
        if (_host.XamlRoot is null)
            return;
        bool ok = await DialogService.ConfirmAsync(_host.XamlRoot,
            "Empty bucket?", "Remove all items from this bucket? Files are not deleted.",
            "Empty", "Cancel");
        if (ok)
            EmptyWithoutPrompt();
    }

    private void EmptyWithoutPrompt()
    {
        var snapshot = Items.Select((item, index) => (item, index)).ToList();
        Items.Clear();
        Selection.Clear();
        SetUndo("Undo Empty", snapshot);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undo is null)
            return;

        foreach (var (item, index) in _undo.Items.OrderBy(t => t.index))
        {
            int target = Math.Clamp(index, 0, Items.Count);
            if (!Items.Any(i => string.Equals(i.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase)))
                Items.Insert(target, item);
        }
        ClearUndo();
    }

    private void SetUndo(string label, List<(FileItem item, int index)> items)
    {
        _undo = new UndoState(label, items);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));
        UndoCommand.NotifyCanExecuteChanged();
    }

    private void ClearUndo()
    {
        _undo = null;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));
        UndoCommand.NotifyCanExecuteChanged();
    }

    // --- copy / move -----------------------------------------------------

    [RelayCommand(CanExecute = nameof(HasItems))]
    private async Task CopyToAsync()
    {
        string? dest = await _host.PickDestinationAsync();
        if (dest is not null)
            await RunOperationAsync(dest, move: false);
    }

    [RelayCommand(CanExecute = nameof(HasItems))]
    private async Task MoveToAsync()
    {
        string? dest = await _host.PickDestinationAsync();
        if (dest is not null)
            await RunOperationAsync(dest, move: true);
    }

    /// <summary>Copies to a pinned destination (right-click offers Move).</summary>
    public Task QuickCopyAsync(QuickDestination dest) => RunOperationAsync(dest.Path, move: false);
    public Task QuickMoveAsync(QuickDestination dest) => RunOperationAsync(dest.Path, move: true);

    [RelayCommand]
    private async Task AddDestinationAsync()
    {
        string? folder = await _host.PickDestinationAsync();
        if (folder is not null)
            QuickDestinationsStore.Add(folder);
    }

    public void RemoveDestination(QuickDestination dest) => QuickDestinationsStore.Remove(dest);

    private async Task RunOperationAsync(string destinationDir, bool move)
    {
        if (Items.Count == 0 || _host.XamlRoot is null || !Directory.Exists(destinationDir))
            return;

        var snapshot = Items.ToList();

        // Resolve name collisions up front with a single decision for the batch.
        int conflicts = snapshot.Count(i =>
        {
            string dest = Path.Combine(destinationDir, i.Name);
            return i.IsFolder ? Directory.Exists(dest) : File.Exists(dest);
        });

        ConflictAction action = ConflictAction.Overwrite;
        if (conflicts > 0)
            action = await DialogService.ResolveConflictAsync(_host.XamlRoot, conflicts);

        _cts = new CancellationTokenSource();
        var progress = new Progress<OperationProgress>(p =>
        {
            ProgressText = p.CurrentItem;
            ProgressValue = p.Completed;
            ProgressMax = Math.Max(1, p.Total);
        });

        ProgressText = "Preparing…";
        ProgressValue = 0;
        IsBusy = true;

        OperationResult result;
        try
        {
            result = await FileOperationsService.RunAsync(
                snapshot, destinationDir, move, action, progress, _cts.Token);
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }

        await ReportResultAsync(result, move);
    }

    // --- transform-on-export: zip / flatten / batch-rename --------------

    [RelayCommand(CanExecute = nameof(HasItems))]
    private Task ExportZip()
        => RunExportAsync((items, dest, p, ct) => FileOperationsService.ZipAsync(items, dest, p, ct));

    [RelayCommand(CanExecute = nameof(HasItems))]
    private Task ExportFlatten()
        => RunExportAsync((items, dest, p, ct) =>
               FileOperationsService.CopyFlatAsync(items, dest, null, ConflictAction.KeepBoth, p, ct));

    [RelayCommand(CanExecute = nameof(HasItems))]
    private async Task ExportRename()
    {
        if (_host.XamlRoot is null)
            return;
        string? prefix = await DialogService.PromptTextAsync(
            _host.XamlRoot, "Batch rename & copy", "Name prefix, e.g. photo_", "file_");
        if (string.IsNullOrWhiteSpace(prefix))
            return;
        await RunExportAsync((items, dest, p, ct) =>
            FileOperationsService.CopyFlatAsync(items, dest, prefix, ConflictAction.KeepBoth, p, ct));
    }

    private async Task RunExportAsync(
        Func<IReadOnlyList<FileItem>, string, IProgress<OperationProgress>, CancellationToken, Task<OperationResult>> op)
    {
        if (Items.Count == 0 || _host.XamlRoot is null)
            return;
        string? dest = await _host.PickDestinationAsync();
        if (dest is null || !Directory.Exists(dest))
            return;

        var snapshot = Items.ToList();
        _cts = new CancellationTokenSource();
        var progress = new Progress<OperationProgress>(p =>
        {
            ProgressText = p.CurrentItem;
            ProgressValue = p.Completed;
            ProgressMax = Math.Max(1, p.Total);
        });
        ProgressText = "Preparing…";
        ProgressValue = 0;
        IsBusy = true;

        OperationResult result;
        try { result = await op(snapshot, dest, progress, _cts.Token); }
        finally { IsBusy = false; _cts.Dispose(); _cts = null; }

        await ReportResultAsync(result, move: false);
    }

    private async Task ReportResultAsync(OperationResult result, bool move)
    {
        if (_host.XamlRoot is null)
            return;

        if (result.Errors.Count > 0)
        {
            string detail = string.Join("\n", result.Errors.Take(8));
            if (result.Errors.Count > 8)
                detail += $"\n…and {result.Errors.Count - 8} more.";
            await DialogService.MessageAsync(_host.XamlRoot, result.Summary(), detail);
        }

        if (result.Succeeded == 0 || result.Canceled)
            return;

        string prompt = move
            ? $"{result.Summary()}\nRemove the moved items from this bucket?"
            : $"{result.Summary()}\nEmpty this bucket?";
        bool empty = await DialogService.ConfirmAsync(_host.XamlRoot,
            move ? "Move complete" : "Copy complete", prompt,
            move ? "Remove items" : "Empty bucket", "Keep items");
        if (empty)
            EmptyWithoutPrompt();
    }

    [RelayCommand]
    private void CancelOperation() => _cts?.Cancel();

    // --- window / view options ------------------------------------------

    [RelayCommand]
    private void NewBucket() => _host.CreateBucketNearMe();

    public void SetView(ViewMode mode)
    {
        CurrentView = mode;
        AppSettings.DefaultViewMode = (int)mode;
        _host.ApplyViewMode(mode);
    }

    public void SetColor(BucketColor color)
    {
        Color = color;
        AccentBrush = new SolidColorBrush(ColorPalette.ToColor(color));
        BackgroundBrush = new SolidColorBrush(ColorPalette.ToBackground(color));
        TitleBarBrush = BuildTitleBarBrush(color);
    }

    private static Brush BuildTitleBarBrush(BucketColor color)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop { Color = ColorPalette.ToHeader(color), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = ColorPalette.ToBackground(color), Offset = 1 });
        return brush;
    }

    partial void OnAlwaysOnTopChanged(bool value) => _host.SetAlwaysOnTop(value);

    [RelayCommand]
    private void ToggleAlwaysOnTop() => AlwaysOnTop = !AlwaysOnTop;

    /// <summary>Toggles between the compact and mid window sizes (animated).</summary>
    [RelayCommand]
    private void ToggleMode()
        => SetMode(Mode == WindowMode.Compact ? WindowMode.Mid : WindowMode.Compact, animate: true);

    public void SetMode(WindowMode mode, bool animate)
    {
        Mode = mode;
        AppSettings.DefaultWindowMode = (int)mode;
        _host.ApplyWindowMode(mode, animate);
    }

    partial void OnModeChanged(WindowMode value)
    {
        OnPropertyChanged(nameof(IsMid));
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(ToggleModeGlyph));
        OnPropertyChanged(nameof(ToggleModeTip));
    }

    public bool IsDetailed => CurrentView == ViewMode.DetailedList;
    partial void OnCurrentViewChanged(ViewMode value) => OnPropertyChanged(nameof(IsDetailed));

    /// <summary>The bucket's name, or "Bucket" when unnamed.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Bucket" : Name;
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    // --- in-bucket filter -----------------------------------------------

    /// <summary>The list the ListView shows — all items, or the filtered subset.</summary>
    public IEnumerable<FileItem> DisplayItems =>
        string.IsNullOrWhiteSpace(FilterText)
            ? Items
            : Items.Where(i => i.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Reorder is only meaningful on the unfiltered list.</summary>
    public bool CanReorder => string.IsNullOrWhiteSpace(FilterText);

    /// <summary>Show the filter box once the list is big enough to warrant it.</summary>
    public bool ShowFilter => Items.Count >= 6;

    partial void OnFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayItems));
        OnPropertyChanged(nameof(CanReorder));
    }

    /// <summary>Captures this bucket's state for session persistence.</summary>
    public BucketState ToState() => new()
    {
        Paths = Items.Select(i => i.FullPath).ToList(),
        Name = Name,
        Color = Color,
        View = CurrentView,
        Mode = Mode,
        AlwaysOnTop = AlwaysOnTop
    };

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(CountNumber));
        OnPropertyChanged(nameof(CountNoun));
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(DisplayItems));
        OnPropertyChanged(nameof(ShowFilter));
        EmptyCommand.NotifyCanExecuteChanged();
        CopyToCommand.NotifyCanExecuteChanged();
        MoveToCommand.NotifyCanExecuteChanged();
        ExportZipCommand.NotifyCanExecuteChanged();
        ExportFlattenCommand.NotifyCanExecuteChanged();
        ExportRenameCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Single-level undo snapshot.</summary>
    private sealed record UndoState(string Label, List<(FileItem item, int index)> Items);
}
