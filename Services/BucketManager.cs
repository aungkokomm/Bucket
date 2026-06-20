using Bucket.Models;
using Bucket.Views;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Bucket.Services;

/// <summary>
/// Owns the lifetime of every <see cref="BucketWindow"/>. Creates new buckets,
/// positions them with a cascading offset, tracks open windows, persists the
/// session, and exits the app once the last bucket closes.
/// </summary>
public sealed class BucketManager
{
    public static BucketManager Current { get; } = new();

    private readonly List<BucketWindow> _windows = new();
    private readonly Dictionary<Guid, BucketState> _snapshots = new();

    private const int Offset = 40;

    // The colors chosen for the project. New buckets cycle through these in order,
    // so the first bucket is Blue (light blue), the next Green, and so on.
    private static readonly BucketColor[] ColorCycle =
    {
        BucketColor.Blue, BucketColor.Green, BucketColor.Orange, BucketColor.Purple, BucketColor.Red
    };

    private int _colorIndex;
    private PointInt32 _nextPosition = new(160, 120);
    private bool _exiting;

    // Thread-safe immutable snapshot of bucket names so the tray thread can read it
    // without touching the UI-thread _windows list.
    private volatile string[] _bucketNames = Array.Empty<string>();

    public IReadOnlyList<BucketWindow> Windows => _windows;

    /// <summary>Open bucket names (snapshot) — safe to read from the tray thread.</summary>
    public string[] BucketNames => _bucketNames;

    /// <summary>Re-snapshots bucket names (call on the UI thread after any change).</summary>
    public void NotifyBucketsChanged() => _bucketNames = _windows.Select(w => w.DisplayName).ToArray();

    /// <summary>Updates the tray tooltip with the total staged item count.</summary>
    public void UpdateTrayBadge()
    {
        int total = _windows.Sum(w => w.ViewModel.Items.Count);
        Tray?.UpdateTooltip(total == 0
            ? "Bucket — file staging shelf"
            : $"Bucket — {total} item{(total == 1 ? "" : "s")}");
    }

    /// <summary>Brings the bucket at <paramref name="index"/> forward (from the tray list).</summary>
    public void ActivateBucket(int index)
    {
        if (index >= 0 && index < _windows.Count)
            _windows[index].ShowFromTray();
    }

    /// <summary>
    /// Shake-to-summon: jumps a bucket to the cursor so you can drop onto it. Reuses
    /// the most-recent bucket (un-hiding it) or creates one if none exist.
    /// </summary>
    public void SummonAt(int cursorX, int cursorY)
    {
        BucketWindow target = _windows.Count > 0 ? _windows[^1] : CreateBucket();
        target.MoveToCursorAndShow(cursorX, cursorY);
    }

    /// <summary>The tray presence, set by <c>App</c> at startup. May be null.</summary>
    public TrayService? Tray { get; set; }

    private SettingsWindow? _settings;
    private EdgeCatcherWindow? _edgeCatcher;

    /// <summary>Creates or removes the screen-edge catcher to match the setting.</summary>
    public void ApplyEdgeCatcher()
    {
        if (AppSettings.EdgeCatcher && _edgeCatcher is null)
        {
            _edgeCatcher = new EdgeCatcherWindow(this);
            _edgeCatcher.Closed += (_, _) => _edgeCatcher = null;
        }
        else if (!AppSettings.EdgeCatcher && _edgeCatcher is not null)
        {
            EdgeCatcherWindow toClose = _edgeCatcher;
            _edgeCatcher = null;
            toClose.Close();
        }
    }

    /// <summary>Content caught by the edge tab: add it to a bucket and bring it forward.</summary>
    public void CatchContent(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;
        BucketWindow target = _windows.Count > 0 ? _windows[^1] : CreateBucket();
        target.ViewModel.AddPaths(paths);
        target.ShowFromTray();
    }

    /// <summary>Opens the settings window (single instance) and brings it forward.</summary>
    public void ShowSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(this);
            _settings.Closed += (_, _) => _settings = null;
        }
        _settings.Activate();
    }

    /// <summary>Applies a new window opacity to every open bucket.</summary>
    public void ApplyOpacityToAll(int percent)
    {
        foreach (BucketWindow window in _windows)
            window.ApplyOpacity(percent);
    }

    /// <summary>
    /// Creates and shows a bucket. When <paramref name="near"/> is given, the new
    /// window cascades from that point (+40,+40); otherwise it uses the running
    /// cascade position.
    /// </summary>
    public BucketWindow CreateBucket(BucketState? state = null, PointInt32? near = null)
    {
        // A brand-new bucket (no restored state) gets the next color in the cycle.
        if (state is null)
        {
            state = new BucketState
            {
                Color = ColorCycle[_colorIndex % ColorCycle.Length],
                View = (ViewMode)AppSettings.DefaultViewMode,
                Mode = (WindowMode)AppSettings.DefaultWindowMode,
                AlwaysOnTop = true
            };
            _colorIndex++;
        }

        var window = new BucketWindow(this, state);
        _windows.Add(window);

        // `near` (from the + button) is the exact top-left to use — it's already
        // computed to sit beside the current bucket. Otherwise cascade.
        PointInt32 pos;
        if (near is { } p)
        {
            pos = p;
        }
        else
        {
            pos = _nextPosition;
            AdvanceCascade(pos);
        }

        SizeInt32 size = window.ModeSize(state.Mode);
        window.AppWindow.MoveAndResize(new RectInt32(pos.X, pos.Y, size.Width, size.Height));
        window.Activate();
        NotifyBucketsChanged();
        return window;
    }

    /// <summary>Restores buckets saved from a previous session, if any.</summary>
    public void RestoreSession()
    {
        foreach (BucketState state in SessionStore.Load())
            CreateBucket(state);
        // Restored buckets hold references only; once shown we no longer need the
        // on-disk copy. It will be rewritten as buckets close with items.
        SessionStore.Clear();
        _snapshots.Clear();
    }

    /// <summary>Called by a window as it closes, supplying its final state.</summary>
    public void OnBucketClosed(BucketWindow window, BucketState finalState)
    {
        _windows.Remove(window);
        NotifyBucketsChanged();
        UpdateTrayBadge();

        if (AppSettings.RestoreSessionEnabled && finalState.Paths.Count > 0)
            _snapshots[window.Id] = finalState;
        else
            _snapshots.Remove(window.Id);

        SessionStore.Save(_snapshots.Values);

        if (_exiting || _windows.Count > 0)
            return;

        // Last window closed. Stay alive in the tray if enabled; otherwise exit.
        if (AppSettings.KeepRunningInTray && Tray is not null)
            return;

        ExitApp();
    }

    /// <summary>
    /// Decides whether a closing window should hide to the tray instead of closing.
    /// Closing the LAST visible bucket would otherwise lose it / exit the app, so we
    /// hide it instead and keep it (with its contents) alive in the tray.
    /// </summary>
    public bool ShouldHideOnClose(BucketWindow window)
    {
        if (_exiting || !AppSettings.KeepRunningInTray || Tray is null)
            return false;
        // If other buckets are still visible, an explicit close really closes this one.
        return _windows.Count(w => !w.IsHiddenToTray) <= 1;
    }

    /// <summary>Shows all buckets (un-hiding any in the tray), or creates one if none exist.</summary>
    public void ShowOrCreate()
    {
        if (_windows.Count == 0)
        {
            CreateBucket();
            return;
        }
        foreach (BucketWindow window in _windows.ToList())
            window.ShowFromTray();
    }

    /// <summary>Fully exits the app: persists open buckets, removes the tray, quits.</summary>
    public void ExitApp()
    {
        if (_exiting)
            return;
        _exiting = true;

        // Capture any still-open buckets so a restart can offer them back.
        if (AppSettings.RestoreSessionEnabled)
        {
            foreach (BucketWindow window in _windows)
            {
                BucketState state = window.ViewModel.ToState();
                if (state.Paths.Count > 0)
                    _snapshots[window.Id] = state;
            }
            SessionStore.Save(_snapshots.Values);
        }

        _edgeCatcher?.Close();
        _edgeCatcher = null;
        Tray?.Dispose();
        Tray = null;
        Application.Current.Exit();
    }

    private void AdvanceCascade(PointInt32 used)
    {
        var next = new PointInt32(used.X + Offset, used.Y + Offset);
        // Wrap the cascade so windows don't march off-screen.
        if (next.X > 900) next.X = 160;
        if (next.Y > 600) next.Y = 120;
        _nextPosition = next;
    }
}
