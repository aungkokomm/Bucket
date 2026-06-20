using System.Diagnostics;
using System.Runtime.InteropServices;
using Bucket.Models;
using Bucket.Services;
using Bucket.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace Bucket.Views;

/// <summary>
/// An independent bucket window. Owns a <see cref="BucketViewModel"/> and supplies
/// it window-level services (pickers, drag/drop, view chrome) via <see cref="IBucketHost"/>.
/// </summary>
public sealed partial class BucketWindow : Window, IBucketHost
{
    private readonly BucketManager _manager;

    public BucketViewModel ViewModel { get; }

    /// <summary>Stable identity used by the manager to track session snapshots.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    // Square window sizes in DIPs (scaled to physical pixels per monitor).
    private const int CompactSideDip = 150;
    private const int MidWidthDip = 480;  // wide enough for the full toolbar
    private const int MidHeightDip = 400;

    private DispatcherQueueTimer? _resizeAnim;

    public BucketWindow(BucketManager manager, BucketState? state = null)
    {
        _manager = manager;
        InitializeComponent();

        ViewModel = new BucketViewModel(this, state);

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        Title = "Bucket";

        // Captionless window: no system minimize/maximize/close. Those actions live
        // in the title bar's right-click menu. We draw our own title bar and move the
        // window manually.
        ConfigureWindowChrome();
        AllowTinyWindow();
        TitleIcon.Source = LoadAppImage("StoreLogo.png");

        // Drag + double-click-to-toggle on both the title bar and the compact panel.
        WireDrag(TitleBarArea);
        WireDrag(CompactPanel);
        TitleBarArea.DoubleTapped += TitleBar_DoubleTapped;
        CompactPanel.DoubleTapped += TitleBar_DoubleTapped;

        // Keep the tray tooltip's item count in sync.
        ViewModel.Items.CollectionChanged += (_, _) => _manager.UpdateTrayBadge();

        ApplyAlwaysOnTop(ViewModel.AlwaysOnTop);
        ApplyOpacity(AppSettings.WindowOpacity);
        ApplyViewMode(ViewModel.CurrentView);
        SyncViewRadio(ViewModel.CurrentView);

        // Size to the current window mode once content loads (the display scale is
        // unreliable during construction) and again whenever the scale settles or
        // the window moves to another monitor.
        Root.Loaded += OnRootLoaded;
        AppWindow.Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>True while this window is hidden in the tray (closed-to-tray).</summary>
    public bool IsHiddenToTray { get; private set; }

    // Closing the last visible bucket hides it to the tray instead of closing, so an
    // accidental close can't lose the bucket or quit the app.
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_manager.ShouldHideOnClose(this))
        {
            args.Cancel = true;
            IsHiddenToTray = true;
            AppWindow.Hide();
        }
    }

    /// <summary>Un-hides the window from the tray (if hidden) and brings it forward.</summary>
    public void ShowFromTray()
    {
        if (IsHiddenToTray)
        {
            IsHiddenToTray = false;
            AppWindow.Show();
        }
        Activate();
    }

    /// <summary>Moves the bucket to the cursor (centered, just below it) and shows it.</summary>
    public void MoveToCursorAndShow(int cursorX, int cursorY)
    {
        if (IsHiddenToTray)
        {
            IsHiddenToTray = false;
            AppWindow.Show();
        }
        Windows.Graphics.SizeInt32 size = AppWindow.Size;
        int nx = cursorX - size.Width / 2;
        int ny = cursorY - 30; // cursor near the top so a drop lands inside the window

        Windows.Graphics.RectInt32 work = DisplayArea
            .GetFromPoint(new Windows.Graphics.PointInt32(cursorX, cursorY), DisplayAreaFallback.Nearest).WorkArea;
        nx = Math.Clamp(nx, work.X, Math.Max(work.X, work.X + work.Width - size.Width));
        ny = Math.Clamp(ny, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height));

        AppWindow.Move(new Windows.Graphics.PointInt32(nx, ny));
        Activate();
    }

    /// <summary>Hides the window into the tray on demand (context-menu action).</summary>
    public void MinimizeToTray()
    {
        if (_manager.Tray is not null)
        {
            IsHiddenToTray = true;
            AppWindow.Hide();
        }
        else if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.Minimize(); // no tray — fall back to a normal minimize
        }
    }

    /// <summary>Applies window opacity (40–100%) via a layered window; 100% = normal.</summary>
    public void ApplyOpacity(int percent)
    {
        percent = Math.Clamp(percent, 40, 100);
        nint ex = GetWindowLongPtr(Hwnd, GWL_EXSTYLE);
        if (percent >= 100)
        {
            SetWindowLongPtr(Hwnd, GWL_EXSTYLE, (nint)((long)ex & ~WS_EX_LAYERED));
        }
        else
        {
            SetWindowLongPtr(Hwnd, GWL_EXSTYLE, (nint)((long)ex | WS_EX_LAYERED));
            SetLayeredWindowAttributes(Hwnd, 0, (byte)(percent * 255 / 100), LWA_ALPHA);
        }
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 0x00000002;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte alpha, uint flags);

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= OnRootLoaded;
        ApplyWindowMode(ViewModel.Mode, animate: false);
        ApplyOpacity(AppSettings.WindowOpacity); // re-apply once shown so the saved value sticks
    }

    // The content scale WinUI uses for layout. It can read 1.0 momentarily at
    // construction/load on a high-DPI monitor and then settle (a WM_DPICHANGED,
    // which we handle to re-apply the size).
    private double Scale => Root.XamlRoot?.RasterizationScale is double s && s > 0 ? s : 1.0;

    /// <summary>Public for the manager so the first frame opens at the right size.</summary>
    public SizeInt32 ModeSize(WindowMode mode)
    {
        // Compact is a small square; mid is wider than tall so the whole toolbar fits.
        (int w, int h) = mode == WindowMode.Compact
            ? (CompactSideDip, CompactSideDip)
            : (MidWidthDip, MidHeightDip);
        return new SizeInt32((int)Math.Round(w * Scale), (int)Math.Round(h * Scale));
    }

    /// <summary>Resizes to the mode's size, optionally with a smooth animation.</summary>
    public void ApplyWindowMode(WindowMode mode, bool animate)
    {
        SizeInt32 target = ModeSize(mode);
        _resizeAnim?.Stop();
        _resizeAnim = null;

        if (!animate)
        {
            AppWindow.Resize(target);
            return;
        }

        int startW = AppWindow.Size.Width;
        int startH = AppWindow.Size.Height;
        if (startW == target.Width && startH == target.Height)
            return;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        const double durationMs = 160;
        _resizeAnim = App.DispatcherQueue.CreateTimer();
        _resizeAnim.Interval = TimeSpan.FromMilliseconds(12);
        _resizeAnim.Tick += (s, _) =>
        {
            double t = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / durationMs);
            double eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
            int w = (int)Math.Round(startW + (target.Width - startW) * eased);
            int h = (int)Math.Round(startH + (target.Height - startH) * eased);
            AppWindow.Resize(new SizeInt32(w, h));
            if (t >= 1.0)
            {
                s.Stop();
                _resizeAnim = null;
                AppWindow.Resize(target);
            }
        };
        _resizeAnim.Start();
    }

    /// <summary>Removes the system title bar / caption buttons for a clean window.</summary>
    private void ConfigureWindowChrome()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true; // reachable from the right-click menu
        }
    }

    // --- custom title bar drag + window-action menu ---------------------

    // Manual window move. We DON'T use the OS move loop (WM_NCLBUTTONDOWN/HTCAPTION):
    // initiated from PointerPressed it intermittently lands in the keyboard move/size
    // mode (double-headed cursor, click-to-place). Instead we capture the pointer and
    // reposition the window by the cursor delta — works the same on every monitor since
    // GetCursorPos and AppWindow.Position are both physical screen pixels.

    private bool _dragging;
    private POINT _dragPointerStart;
    private Windows.Graphics.PointInt32 _dragWindowStart;
    private UIElement? _dragElement;
    private Pointer? _dragPointer;

    private void WireDrag(UIElement element)
    {
        element.PointerPressed += Drag_PointerPressed;
        element.PointerMoved += Drag_PointerMoved;
        element.PointerReleased += Drag_PointerReleased;
        element.PointerCaptureLost += Drag_PointerCaptureLost;
        element.PointerCanceled += Drag_PointerCaptureLost;
    }

    private void Drag_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint((UIElement)sender).Properties.IsLeftButtonPressed)
            return;
        if (IsInteractive(e.OriginalSource as DependencyObject))
            return; // let the + button (etc.) handle its own click

        EndDrag(); // clear any stranded capture from a previous drag first

        var element = (UIElement)sender;
        if (element.CapturePointer(e.Pointer))
        {
            _dragging = true;
            _dragElement = element;
            _dragPointer = e.Pointer;
            GetCursorPos(out _dragPointerStart);
            _dragWindowStart = AppWindow.Position;
        }
    }

    private void Drag_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
            return;
        // If the button came up without us seeing PointerReleased, stop now — this is
        // what used to leave the window "stuck" with a stranded capture.
        if (!e.GetCurrentPoint((UIElement)sender).Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }
        GetCursorPos(out POINT cur);
        AppWindow.Move(new Windows.Graphics.PointInt32(
            _dragWindowStart.X + (cur.X - _dragPointerStart.X),
            _dragWindowStart.Y + (cur.Y - _dragPointerStart.Y)));
    }

    private void Drag_PointerReleased(object sender, PointerRoutedEventArgs e) => EndDrag();
    private void Drag_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndDrag();

    private void EndDrag()
    {
        // Null state before releasing capture so the reentrant PointerCaptureLost is a no-op.
        UIElement? element = _dragElement;
        Pointer? pointer = _dragPointer;
        _dragging = false;
        _dragElement = null;
        _dragPointer = null;
        if (element is not null && pointer is not null)
            element.ReleasePointerCapture(pointer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    private static bool IsInteractive(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is ButtonBase)
                return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter p)
            p.Minimize();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e) => MinimizeToTray();

    private void Settings_Click(object sender, RoutedEventArgs e) => _manager.ShowSettings();

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(new Image
        {
            Source = TitleIcon.Source,
            Width = 44,
            Height = 44,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = "Bucket",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        titleStack.Children.Add(new TextBlock { Text = "Version 1.0.0", FontSize = 12, Opacity = 0.7 });
        header.Children.Add(titleStack);

        var panel = new StackPanel { Spacing = 4, Width = 320 };
        panel.Children.Add(header);
        panel.Children.Add(new TextBlock
        {
            Text = "A portable file-staging shelf for Windows.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 4),
        });
        panel.Children.Add(new HyperlinkButton
        {
            Content = "GitHub repository",
            NavigateUri = new Uri("https://github.com/aungkokomm/Bucket"),
            Padding = new Thickness(0),
        });
        panel.Children.Add(new HyperlinkButton
        {
            Content = "More of my projects",
            NavigateUri = new Uri("https://aungkokomm.github.io/"),
            Padding = new Thickness(0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "© 2026 Aung Ko Ko · MIT License",
            FontSize = 12,
            Opacity = 0.7,
            Margin = new Thickness(0, 8, 0, 0),
        });

        var dialog = new ContentDialog
        {
            Title = "About",
            Content = panel,
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
        };
        try { await dialog.ShowAsync(); }
        catch { /* a dialog is already open — ignore */ }
    }

    /// <summary>The bucket's display name, for the tray's bucket list.</summary>
    public string DisplayName => ViewModel.DisplayName;

    private async void RenameBucket_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
            return;
        string? name = await DialogService.PromptTextAsync(XamlRoot, "Rename bucket", "Bucket name", ViewModel.Name);
        if (name is null)
            return;
        ViewModel.Name = name.Trim();
        _manager.NotifyBucketsChanged();
    }

    private static BitmapImage LoadAppImage(string fileName)
        => new(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", fileName)));

    // --- window subclass: allow tiny sizes + keep the window square ------
    // Windows enforces a global minimum window size (SM_CXMIN) and won't keep an
    // aspect ratio on its own. We shrink ptMinTrackSize and square up WM_SIZING.

    private SUBCLASSPROC? _subclassProc; // held to keep the delegate from being collected

    // Maps each window's HWND to its instance so the static subclass proc can call back.
    private static readonly Dictionary<nint, BucketWindow> Instances = new();

    private void AllowTinyWindow()
    {
        Instances[Hwnd] = this;
        _subclassProc = WindowSubclass;
        SetWindowSubclass(Hwnd, _subclassProc, 1, 0);
    }

    private static nint WindowSubclass(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        const uint WM_GETMINMAXINFO = 0x0024;
        const uint WM_DPICHANGED = 0x02E0;

        if (msg == WM_DPICHANGED && Instances.TryGetValue(hWnd, out BucketWindow? win))
        {
            // The display scale just settled/changed — re-apply the mode's size at the
            // new scale (RasterizationScale is correct by the next tick).
            nint result = DefSubclassProc(hWnd, msg, wParam, lParam);
            App.DispatcherQueue.TryEnqueue(() =>
            {
                if (win._resizeAnim is null)
                    win.ApplyWindowMode(win.ViewModel.Mode, animate: false);
            });
            return result;
        }

        if (msg == WM_GETMINMAXINFO)
        {
            // Allow small windows; aspect ratio is free (no square lock).
            MINMAXINFO mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.ptMinTrackSize.x = 120;
            mmi.ptMinTrackSize.y = 90;
            Marshal.StructureToPtr(mmi, lParam, false);
            return 0;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private delegate nint SUBCLASSPROC(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint data);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SUBCLASSPROC proc, nuint id, nuint refData);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINTL ptReserved;
        public POINTL ptMaxSize;
        public POINTL ptMaxPosition;
        public POINTL ptMinTrackSize;
        public POINTL ptMaxTrackSize;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Instances.Remove(Hwnd);
        _manager.OnBucketClosed(this, ViewModel.ToState());
    }

    // --- IBucketHost -----------------------------------------------------

    public XamlRoot? XamlRoot => Content?.XamlRoot;

    private nint Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        return files.Where(f => !string.IsNullOrEmpty(f.Path)).Select(f => f.Path).ToList();
    }

    public async Task<string?> PickFolderToAddAsync() => await PickFolderAsync();
    public async Task<string?> PickDestinationAsync() => await PickFolderAsync();

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public void SetAlwaysOnTop(bool value) => ApplyAlwaysOnTop(value);

    private void ApplyAlwaysOnTop(bool value)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = value;
    }

    public void CreateBucketNearMe()
    {
        // Place the new bucket beside this one (to the right, with a gap), not on top.
        // Fall back to the left, then clamp, if there's no room on the chosen monitor.
        Windows.Graphics.PointInt32 pos = AppWindow.Position;
        Windows.Graphics.SizeInt32 size = AppWindow.Size;
        const int gap = 12;

        Windows.Graphics.RectInt32 work =
            DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;

        int rightX = pos.X + size.Width + gap;
        int newX;
        if (rightX + size.Width <= work.X + work.Width)
            newX = rightX;                                       // room on the right
        else if (pos.X - size.Width - gap >= work.X)
            newX = pos.X - size.Width - gap;                     // else on the left
        else
            newX = Math.Max(work.X, work.X + work.Width - size.Width); // clamp on screen

        int newY = Math.Clamp(pos.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height));
        _manager.CreateBucket(near: new Windows.Graphics.PointInt32(newX, newY));
    }

    /// <summary>
    /// Swaps the item template/panel for the chosen list view and toggles the
    /// detailed-view column header. Does not affect window size (that's mode-driven).
    /// </summary>
    public void ApplyViewMode(ViewMode mode)
    {
        switch (mode)
        {
            case ViewMode.MiniStrip:
                ItemsView.ItemTemplate = (DataTemplate)Root.Resources["MiniTemplate"];
                ItemsView.ItemsPanel = (ItemsPanelTemplate)Root.Resources["PanelHorizontal"];
                SetScroll(horizontal: true);
                break;
            case ViewMode.CompactList:
                ItemsView.ItemTemplate = (DataTemplate)Root.Resources["CompactTemplate"];
                ItemsView.ItemsPanel = (ItemsPanelTemplate)Root.Resources["PanelVertical"];
                SetScroll(horizontal: false);
                break;
            case ViewMode.DetailedList:
                ItemsView.ItemTemplate = (DataTemplate)Root.Resources["DetailedTemplate"];
                ItemsView.ItemsPanel = (ItemsPanelTemplate)Root.Resources["PanelVertical"];
                SetScroll(horizontal: false);
                break;
            case ViewMode.Gallery:
                ItemsView.ItemTemplate = (DataTemplate)Root.Resources["GalleryTemplate"];
                ItemsView.ItemsPanel = (ItemsPanelTemplate)Root.Resources["PanelWrap"];
                SetScroll(horizontal: false);
                break;
        }
    }

    private void SetScroll(bool horizontal)
    {
        ScrollViewer.SetHorizontalScrollMode(ItemsView, horizontal ? ScrollMode.Enabled : ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(ItemsView, horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollMode(ItemsView, horizontal ? ScrollMode.Disabled : ScrollMode.Enabled);
        ScrollViewer.SetVerticalScrollBarVisibility(ItemsView, horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
    }

    // --- title bar double-click toggles compact/mid ---------------------

    private void TitleBar_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IsInteractive(e.OriginalSource as DependencyObject))
            return;
        ViewModel.ToggleModeCommand.Execute(null);
    }

    // --- drag and drop (into the bucket) --------------------------------

    private void Content_DragEnter(object sender, DragEventArgs e) => UpdateDragState(e, entering: true);
    private void Content_DragOver(object sender, DragEventArgs e) => UpdateDragState(e, entering: true);
    private void Content_DragLeave(object sender, DragEventArgs e) => DropHint.Visibility = Visibility.Collapsed;

    // Files, text, images and links are all accepted.
    private static bool AcceptsContent(DataPackageView view)
        => view.Contains(StandardDataFormats.StorageItems)
        || view.Contains(StandardDataFormats.Bitmap)
        || view.Contains(StandardDataFormats.WebLink)
        || view.Contains(StandardDataFormats.ApplicationLink)
        || view.Contains(StandardDataFormats.Text);

    private void UpdateDragState(DragEventArgs e, bool entering)
    {
        if (AcceptsContent(e.DataView))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to bucket";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
            DropHint.Visibility = Visibility.Visible;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void Content_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (!AcceptsContent(e.DataView))
            return;

        DragOperationDeferral deferral = e.GetDeferral();
        try
        {
            ViewModel.AddPaths(await ContentStager.StageAsync(e.DataView));
        }
        finally
        {
            deferral.Complete();
        }
    }

    // --- selection / context menu ---------------------------------------

    private void ItemsView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.Selection.Clear();
        ViewModel.Selection.AddRange(ItemsView.SelectedItems.OfType<FileItem>());
        ViewModel.RemoveSelectedCommand.NotifyCanExecuteChanged();
    }

    private void ItemsView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Ensure the right-clicked row is part of the selection the menu acts on.
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileItem item &&
            !ItemsView.SelectedItems.Contains(item))
        {
            ItemsView.SelectedItem = item;
        }
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
        => ViewModel.RemoveSelectedCommand.Execute(null);

    // --- per-item actions: drag-out, open, reveal, copy path -------------

    // Drag items OUT to Explorer / other apps. Storage items are produced lazily via a
    // data provider so the (async) StorageFile lookups happen at drop time.
    private void ItemsView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<FileItem>().Select(i => i.FullPath).ToList();
        if (paths.Count == 0)
        {
            e.Cancel = true;
            return;
        }
        e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            DataProviderDeferral deferral = request.GetDeferral();
            try
            {
                var items = new List<IStorageItem>();
                foreach (string p in paths)
                {
                    try
                    {
                        if (Directory.Exists(p)) items.Add(await StorageFolder.GetFolderFromPathAsync(p));
                        else if (File.Exists(p)) items.Add(await StorageFile.GetFileFromPathAsync(p));
                    }
                    catch { /* skip inaccessible item */ }
                }
                request.SetData(items);
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    // External files dropped onto the list (which owns drops for reordering) still add.
    private void ItemsView_DragOver(object sender, DragEventArgs e)
    {
        if (AcceptsContent(e.DataView))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to bucket";
            e.DragUIOverride.IsCaptionVisible = true;
        }
        // otherwise leave it to the built-in reorder
    }

    private async void ItemsView_Drop(object sender, DragEventArgs e)
    {
        if (!AcceptsContent(e.DataView))
            return; // a reorder — let the ListView handle it
        e.Handled = true;
        DragOperationDeferral deferral = e.GetDeferral();
        try
        {
            ViewModel.AddPaths(await ContentStager.StageAsync(e.DataView));
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ItemsView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileItem item)
            ShellOpen(item.FullPath);
    }

    private void OpenItem_Click(object sender, RoutedEventArgs e)
    {
        foreach (FileItem item in ItemsView.SelectedItems.OfType<FileItem>().ToList())
            ShellOpen(item.FullPath);
    }

    private void RevealItem_Click(object sender, RoutedEventArgs e)
    {
        FileItem? item = ItemsView.SelectedItems.OfType<FileItem>().FirstOrDefault();
        if (item is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        var paths = ItemsView.SelectedItems.OfType<FileItem>().Select(i => i.FullPath).ToList();
        if (paths.Count == 0)
            return;
        var data = new DataPackage();
        data.SetText(string.Join(Environment.NewLine, paths));
        Clipboard.SetContent(data);
    }

    private static void ShellOpen(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    // --- menu handlers ---------------------------------------------------

    private void AddFiles_Click(object sender, RoutedEventArgs e) => ViewModel.AddFilesCommand.Execute(null);
    private void AddFolder_Click(object sender, RoutedEventArgs e) => ViewModel.AddFolderCommand.Execute(null);

    private void View_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tag && Enum.TryParse(tag, out ViewMode mode))
            ViewModel.SetView(mode);
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tag && Enum.TryParse(tag, out BucketColor color))
            ViewModel.SetColor(color);
    }

    private void SendToFlyout_Opening(object sender, object e)
    {
        SendToFlyout.Items.Clear();

        if (ViewModel.QuickDestinations.Count == 0)
        {
            SendToFlyout.Items.Add(new MenuFlyoutItem { Text = "No pinned folders", IsEnabled = false });
        }
        else
        {
            foreach (QuickDestination dest in ViewModel.QuickDestinations)
            {
                var sub = new MenuFlyoutSubItem { Text = dest.Name };

                var copy = new MenuFlyoutItem { Text = "Copy here" };
                copy.Click += async (_, _) => await ViewModel.QuickCopyAsync(dest);
                sub.Items.Add(copy);

                var move = new MenuFlyoutItem { Text = "Move here" };
                move.Click += async (_, _) => await ViewModel.QuickMoveAsync(dest);
                sub.Items.Add(move);

                sub.Items.Add(new MenuFlyoutSeparator());

                var unpin = new MenuFlyoutItem { Text = "Unpin" };
                unpin.Click += (_, _) => ViewModel.RemoveDestination(dest);
                sub.Items.Add(unpin);

                SendToFlyout.Items.Add(sub);
            }
        }

        SendToFlyout.Items.Add(new MenuFlyoutSeparator());
        var pin = new MenuFlyoutItem { Text = "Pin a folder…" };
        pin.Click += (_, _) => ViewModel.AddDestinationCommand.Execute(null);
        SendToFlyout.Items.Add(pin);
    }

    private void RestoreInfo_Close(InfoBar sender, object args) => ViewModel.RestoredFromSession = false;

    private void FirstRunTip_Close(InfoBar sender, object args) => ViewModel.FirstRunTip = false;

    private void SyncViewRadio(ViewMode mode)
    {
        ViewMini.IsChecked = mode == ViewMode.MiniStrip;
        ViewCompact.IsChecked = mode == ViewMode.CompactList;
        ViewDetailed.IsChecked = mode == ViewMode.DetailedList;
        ViewGallery.IsChecked = mode == ViewMode.Gallery;
    }

    // --- keyboard accelerators ------------------------------------------

    private void Delete_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.RemoveSelectedCommand.Execute(null);
        args.Handled = true;
    }

    private void Paste_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.PasteCommand.Execute(null);
        args.Handled = true;
    }

    private void NewBucket_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.NewBucketCommand.Execute(null);
        args.Handled = true;
    }

    private void Undo_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.UndoCommand.Execute(null);
        args.Handled = true;
    }
}
