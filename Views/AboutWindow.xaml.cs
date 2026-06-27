using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace Bucket.Views;

/// <summary>
/// A small standalone "About" window. It's a real window (not a ContentDialog) so it
/// can't be clipped by a tiny compact-mode bucket window.
/// </summary>
public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon("Assets/AppIcon.ico");
        ExtendsContentIntoTitleBar = false;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsResizable = false;
        }

        Version? v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? "Version 1.0" : $"Version {v.Major}.{v.Minor}.{v.Build}";
        AppIconImage.Source = new BitmapImage(
            new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "StoreLogo.png")));

        if (Content is FrameworkElement root)
            root.Loaded += OnContentLoaded;
    }

    private void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        ((FrameworkElement)sender).Loaded -= OnContentLoaded;
        double scale = Content.XamlRoot?.RasterizationScale is double s && s > 0 ? s : 1.0;
        AppWindow.Resize(new SizeInt32((int)(380 * scale), (int)(290 * scale)));
    }
}
