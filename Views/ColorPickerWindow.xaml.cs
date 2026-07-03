using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Bucket.Views;

/// <summary>
/// A small standalone window wrapping a WinUI <c>ColorPicker</c>. Standalone (not a
/// flyout/dialog) so it can't be clipped by a compact-mode bucket window. The action
/// buttons live in a pinned bottom row so they are always visible.
/// </summary>
public sealed partial class ColorPickerWindow : Window
{
    private readonly Action<Windows.UI.Color> _onPicked;

    public ColorPickerWindow(Windows.UI.Color initial, Action<Windows.UI.Color> onPicked)
    {
        _onPicked = onPicked;
        InitializeComponent();

        AppWindow.SetIcon("Assets/AppIcon.ico");
        ExtendsContentIntoTitleBar = false;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsResizable = true;
        }

        Picker.Color = initial;

        if (Content is FrameworkElement root)
            root.Loaded += OnContentLoaded;
    }

    private void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        ((FrameworkElement)sender).Loaded -= OnContentLoaded;
        double scale = Content.XamlRoot?.RasterizationScale is double s && s > 0 ? s : 1.0;
        AppWindow.Resize(new SizeInt32((int)(400 * scale), (int)(660 * scale)));
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        _onPicked(Picker.Color);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
