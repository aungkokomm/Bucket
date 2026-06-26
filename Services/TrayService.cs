using System.Runtime.InteropServices;

namespace Bucket.Services;

/// <summary>
/// System-tray presence on raw Win32 (Shell_NotifyIcon + a hidden window + a native
/// popup menu), hosted on its OWN thread with a classic GetMessage/DispatchMessage
/// loop. H.NotifyIcon and a message-only window on the WinUI UI thread both failed to
/// receive the shell's mouse callbacks under the Windows 11 tray; a dedicated pumped
/// thread is the reliable pattern. Window/menu code runs on the tray thread; actions
/// that touch the UI (create/show windows) marshal to the UI thread.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly BucketManager _manager;
    private WndProc? _wndProc;          // kept alive for the window's lifetime
    private Thread? _thread;
    private nint _hwnd;
    private nint _hicon;
    private bool _added;

    private const uint WM_TRAY = 0x0400 + 1; // WM_APP + 1
    private const int IdNew = 1, IdShow = 2, IdExit = 3;

    public TrayService(BucketManager manager) => _manager = manager;

    private static void Log(string msg)
    {
        try { File.AppendAllText(Storage.PathTo("tray.log"), $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); }
        catch { }
    }

    public void Initialize()
    {
        _thread = new Thread(ThreadProc) { IsBackground = true, Name = "BucketTray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void ThreadProc()
    {
        _wndProc = WndProcImpl;
        nint hInstance = GetModuleHandle(null);

        var wc = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = "BucketTrayWindow"
        };
        RegisterClass(ref wc);

        // A normal (never-shown) window, not HWND_MESSAGE — the shell delivers tray
        // callbacks to it reliably.
        _hwnd = CreateWindowEx(0, "BucketTrayWindow", "BucketTray", 0, 0, 0, 0, 0, 0, 0, hInstance, 0);
        _hicon = LoadHIcon();

        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_TRAY;
        data.hIcon = _hicon;
        data.szTip = "Bucket — file staging shelf";
        _added = Shell_NotifyIcon(NIM_ADD, ref data);

        // Windows 11's tray (especially the overflow flyout) only forwards icon
        // clicks to apps that opt into NOTIFYICON_VERSION_4.
        var ver = NewData();
        ver.uVersion = NOTIFYICON_VERSION_4;
        bool setVer = Shell_NotifyIcon(NIM_SETVERSION, ref ver);
        Log($"NIM_ADD hwnd={_hwnd} added={_added} setVer={setVer}");

        // Global hotkey Ctrl+Shift+B → summon the app from the tray.
        bool hk = RegisterHotKey(_hwnd, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_B);
        Log($"RegisterHotKey Ctrl+Shift+B = {hk}");

        // Low-level mouse hook for shake-to-summon (runs on this pumped thread).
        _mouseProc = MouseHookProc;
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hInstance, 0);
        Log($"mouse hook = {_mouseHook}");

        while (GetMessage(out MSG m, 0, 0, 0) > 0)
        {
            TranslateMessage(ref m);
            DispatchMessage(ref m);
        }
        Log("message loop ended");
    }

    private nint WndProcImpl(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        // Native callback — must never let an exception escape into the OS.
        try
        {
            switch (msg)
            {
                case WM_TRAY:
                    // With NOTIFYICON_VERSION_4, LOWORD(lParam) is the event (NIN_SELECT
                    // for left-click, WM_CONTEXTMENU for right-click). Plain WM_*BUTTONUP
                    // is also handled for the pre-v4 case.
                    uint mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                    Log($"tray msg 0x{mouse:X}");
                    if (mouse is WM_LBUTTONUP or NIN_SELECT or NIN_KEYSELECT)
                        Ui(_manager.ShowOrCreate);
                    else if (mouse is WM_RBUTTONUP or WM_CONTEXTMENU)
                        ShowMenu();
                    return 0;
                case WM_HOTKEY:
                    if ((int)wParam == HotkeyId)
                        Ui(_manager.ShowOrCreate);
                    return 0;
                case WM_DESTROY:
                    PostQuitMessage(0);
                    return 0;
            }
        }
        catch (Exception ex) { CrashLog.Write("tray wndproc", ex); }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // --- shake-to-summon (low-level mouse hook on the tray thread) -------

    private HookProc? _mouseProc;
    private nint _mouseHook;
    private int _shakeLastX;
    private int _shakeDir;
    private readonly Queue<long> _reversals = new();
    private long _lastShakeTick;

    private nint MouseHookProc(int code, nint wParam, nint lParam)
    {
        // This runs on every mouse move; an escaping exception would crash the
        // whole process, so it must never throw.
        try
        {
            if (code >= 0 && (uint)wParam == WM_MOUSEMOVE && AppSettings.ShakeToSummon)
            {
                MSLLHOOKSTRUCT data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (DetectShake(data.pt.X, Environment.TickCount64))
                {
                    int x = data.pt.X, y = data.pt.Y;
                    Ui(() => _manager.SummonAt(x, y));
                }
            }
        }
        catch (Exception ex) { CrashLog.Write("mouse hook", ex); }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    // A "shake" = several quick horizontal direction reversals in a short window.
    private bool DetectShake(int x, long now)
    {
        int dx = x - _shakeLastX;
        _shakeLastX = x;
        if (Math.Abs(dx) < 8)
            return false;

        int dir = Math.Sign(dx);
        if (_shakeDir != 0 && dir != _shakeDir)
            _reversals.Enqueue(now);
        _shakeDir = dir;

        while (_reversals.Count > 0 && now - _reversals.Peek() > 650)
            _reversals.Dequeue();

        if (_reversals.Count >= 4 && now - _lastShakeTick > 1500)
        {
            _lastShakeTick = now;
            _reversals.Clear();
            return true;
        }
        return false;
    }

    private void ShowMenu()
    {
        nint menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, IdNew, "New bucket");
        AppendMenu(menu, MF_STRING, IdShow, "Show buckets");

        // A submenu listing each open bucket by name (click to bring it forward).
        string[] names = _manager.BucketNames;
        if (names.Length > 0)
        {
            nint sub = CreatePopupMenu();
            for (int i = 0; i < names.Length; i++)
            {
                string label = string.IsNullOrWhiteSpace(names[i]) ? "Bucket" : names[i];
                AppendMenu(sub, MF_STRING, (nuint)(BucketIdBase + i), label);
            }
            AppendMenu(menu, MF_POPUP, (nuint)sub, "Buckets");
        }

        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, IdExit, "Exit");

        GetCursorPos(out POINT pt);
        SetForegroundWindow(_hwnd); // required for the menu to behave/dismiss correctly
        int cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, 0);
        PostMessage(_hwnd, 0, 0, 0); // documented WM_NULL workaround
        DestroyMenu(menu); // also destroys the submenu
        Log($"menu cmd={cmd}");

        if (cmd >= BucketIdBase)
        {
            int index = cmd - BucketIdBase;
            Ui(() => _manager.ActivateBucket(index));
            return;
        }
        switch (cmd)
        {
            case IdNew: Ui(() => _manager.CreateBucket()); break;
            case IdShow: Ui(_manager.ShowOrCreate); break;
            case IdExit: Ui(_manager.ExitApp); break;
        }
    }

    private static void Ui(Action action) => App.DispatcherQueue.TryEnqueue(() => action());

    private static nint LoadHIcon()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        return LoadImage(0, path, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
    }

    /// <summary>Updates the tray icon's tooltip (e.g. with the staged item count).</summary>
    public void UpdateTooltip(string tip)
    {
        if (!_added)
            return;
        var data = NewData();
        data.uFlags = NIF_TIP;
        data.szTip = tip;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private NOTIFYICONDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1
    };

    public void Dispose()
    {
        if (_mouseHook != 0)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
        if (_hwnd != 0)
            UnregisterHotKey(_hwnd, HotkeyId);
        if (_added)
        {
            var data = NewData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        if (_hwnd != 0)
        {
            PostMessage(_hwnd, WM_CLOSE, 0, 0); // ends the message loop on the tray thread
            _hwnd = 0;
        }
        if (_hicon != 0) { DestroyIcon(_hicon); _hicon = 0; }
    }

    // --- Win32 interop ---------------------------------------------------

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    private const uint NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401; // WM_USER + 0/1
    private const uint WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B;
    private const uint WM_DESTROY = 0x0002, WM_CLOSE = 0x0010, WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_NOREPEAT = 0x4000;
    private const uint VK_B = 0x42;
    private const int WH_MOUSE_LL = 14;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const int HotkeyId = 0xB0CC; // "BoCC" — arbitrary unique id
    private const uint MF_STRING = 0, MF_SEPARATOR = 0x800, MF_POPUP = 0x0010;
    private const int BucketIdBase = 100;
    private const uint TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x0010, LR_DEFAULTSIZE = 0x0040;

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);
    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WNDCLASS wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, nint parent, nint menu, nint hInstance, nint param);

    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? item);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc proc, nint hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hhk);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hhk, int code, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint hinst, string name, uint type, int cx, int cy, uint load);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint handle);
}
