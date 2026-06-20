using Bucket.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;

namespace Bucket.Views;

/// <summary>
/// A small, standard settings window (normal title bar). Reads and writes
/// <see cref="AppSettings"/> directly and pushes live changes (opacity) through the
/// <see cref="BucketManager"/>.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly BucketManager _manager;
    private bool _loading;

    public SettingsWindow(BucketManager manager)
    {
        _manager = manager;
        InitializeComponent();

        AppWindow.SetIcon("Assets/AppIcon.ico");
        ExtendsContentIntoTitleBar = false;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsMaximizable = false;
            p.IsResizable = true;
        }

        _loading = true;
        TrayToggle.IsOn = AppSettings.KeepRunningInTray;
        RestoreToggle.IsOn = AppSettings.RestoreSessionEnabled;
        ShakeToggle.IsOn = AppSettings.ShakeToSummon;
        EdgeToggle.IsOn = AppSettings.EdgeCatcher;
        OpacitySlider.Value = AppSettings.WindowOpacity;
        OpacityValue.Text = $"{AppSettings.WindowOpacity}%";
        _loading = false;

        if (Content is FrameworkElement root)
            root.Loaded += OnContentLoaded;
    }

    private void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        ((FrameworkElement)sender).Loaded -= OnContentLoaded;
        double scale = Content.XamlRoot?.RasterizationScale is double s && s > 0 ? s : 1.0;
        AppWindow.Resize(new SizeInt32((int)(460 * scale), (int)(620 * scale)));
    }

    private void TrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            AppSettings.KeepRunningInTray = TrayToggle.IsOn;
    }

    private void RestoreToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            AppSettings.RestoreSessionEnabled = RestoreToggle.IsOn;
    }

    private void ShakeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
            AppSettings.ShakeToSummon = ShakeToggle.IsOn;
    }

    private void EdgeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        AppSettings.EdgeCatcher = EdgeToggle.IsOn;
        _manager.ApplyEdgeCatcher();
    }

    private void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        int percent = (int)e.NewValue;
        OpacityValue.Text = $"{percent}%";
        if (_loading)
            return;
        AppSettings.WindowOpacity = percent;
        _manager.ApplyOpacityToAll(percent);
    }
}
