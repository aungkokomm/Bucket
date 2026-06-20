using System.Runtime.InteropServices;
using Bucket.Helpers;
using Bucket.Models;
using Bucket.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI;

namespace Bucket.Views;

/// <summary>
/// A thin always-on-top tab docked to the right screen edge. Dragging files (or text/
/// images/links) onto it adds them to a bucket and brings that bucket forward — the
/// Yoink-style "catch" gesture. It never takes focus and isn't in the taskbar/alt-tab.
/// </summary>
public sealed partial class EdgeCatcherWindow : Window
{
    private readonly BucketManager _manager;
    private SolidColorBrush _idleBrush = null!;
    private SolidColorBrush _activeBrush = null!;

    private const int IdleWidthDip = 30;
    private const int ExpandedWidthDip = 130;
    private const int HeightDip = 140;

    public EdgeCatcherWindow(BucketManager manager)
    {
        _manager = manager;
        InitializeComponent();

        Title = "Bucket Edge Catcher";
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
        }

        // No taskbar button + never steal focus.
        nint ex = GetWindowLongPtr(Hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(Hwnd, GWL_EXSTYLE, (nint)((long)ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));

        Color accent = ColorPalette.ToColor(BucketColor.Blue);
        _idleBrush = new SolidColorBrush(accent) { Opacity = 0.85 };
        _activeBrush = new SolidColorBrush(ColorPalette.ToHeader(BucketColor.Blue));
        Root.Background = _idleBrush;
        TabIcon.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "StoreLogo.png")));

        Root.Loaded += (_, _) => PositionIdle();
        Activate();
    }

    private nint Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);
    private double Scale => Root.XamlRoot?.RasterizationScale is double s && s > 0 ? s : 1.0;

    private void PositionIdle() => DockRight(IdleWidthDip);
    private void Expand() => DockRight(ExpandedWidthDip);

    // Docks the window flush to the right edge of the primary work area, vertically centered.
    private void DockRight(int widthDip)
    {
        double scale = Scale;
        int w = (int)Math.Round(widthDip * scale);
        int h = (int)Math.Round(HeightDip * scale);
        RectInt32 work = DisplayArea.Primary.WorkArea;
        int x = work.X + work.Width - w;
        int y = work.Y + (work.Height - h) / 2;
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    private static bool Accepts(DataPackageView v)
        => v.Contains(StandardDataFormats.StorageItems)
        || v.Contains(StandardDataFormats.Bitmap)
        || v.Contains(StandardDataFormats.WebLink)
        || v.Contains(StandardDataFormats.ApplicationLink)
        || v.Contains(StandardDataFormats.Text);

    private void Root_DragEnter(object sender, DragEventArgs e) => SetActive(e, true);
    private void Root_DragOver(object sender, DragEventArgs e) => SetActive(e, true);
    private void Root_DragLeave(object sender, DragEventArgs e) => SetActive(null, false);

    private void SetActive(DragEventArgs? e, bool active)
    {
        if (e is not null)
        {
            if (!Accepts(e.DataView))
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Catch in bucket";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        Root.Background = active ? _activeBrush : _idleBrush;
        TabHint.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (active)
            Expand();
        else
            PositionIdle();
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        SetActive(null, false);
        if (!Accepts(e.DataView))
            return;
        DragOperationDeferral deferral = e.GetDeferral();
        try
        {
            IReadOnlyList<string> paths = await ContentStager.StageAsync(e.DataView);
            _manager.CatchContent(paths);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
