using Bucket.Services;
using Microsoft.UI.Xaml;

namespace Bucket;

/// <summary>
/// Application entry point. Bootstraps the <see cref="BucketManager"/>, optionally
/// restores the previous session, and ensures at least one bucket is open. The app
/// has no main window — it lives as long as any bucket window is open.
/// </summary>
public partial class App : Application
{
    /// <summary>The dispatcher for the UI thread, for marshaling background work.</summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        BucketManager manager = BucketManager.Current;

        // System-tray presence: keeps the app alive when all bucket windows are
        // closed so it can be re-summoned (and exited) from the tray. Never let a
        // tray failure prevent the app from starting.
        try
        {
            var tray = new TrayService(manager);
            manager.Tray = tray;
            tray.Initialize();
        }
        catch
        {
            manager.Tray = null;
        }

        if (AppSettings.RestoreSessionEnabled && SessionStore.HasSession)
            manager.RestoreSession();

        // Always start with at least one bucket so the app has a window.
        if (manager.Windows.Count == 0)
            manager.CreateBucket();

        try { manager.ApplyEdgeCatcher(); } catch { }
    }
}
