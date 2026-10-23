using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tempo.App.Interop;

namespace Tempo.App.Services;

/// <summary>
/// Closes Tempo's context menus and flyouts when the user clicks anywhere outside Tempo.
/// WPF normally does this via mouse capture, but capture only reaches other apps' windows when
/// the owner is the foreground window, and the bar deliberately never activates.
/// While anything is open, a low-level mouse hook runs on its own thread (so a busy UI thread can
/// never lag the system mouse); it is removed as soon as everything is closed.
/// </summary>
internal static class OutsideClickWatcher
{
    private delegate nint HookProc(int code, nint wParam, nint lParam);

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_XBUTTONDOWN = 0x020B;
    private const int WM_QUIT = 0x0012;
    private const uint GA_ROOT = 2;

    private static readonly Dictionary<object, Action> Open = [];
    private static readonly HashSet<object> Flyouts = [];
    private static readonly object Gate = new();
    private static Dispatcher? _dispatcher;
    private static Thread? _hookThread;
    private static uint _hookThreadId;
    private static HookProc? _proc;

    public static void Initialize(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((sender, _) =>
        {
            var menu = (ContextMenu)sender;
            Register(menu, () => menu.IsOpen = false);
        }));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler((sender, _) => Unregister(sender)));
    }

    /// <summary>Call on the UI thread when a menu opens; <paramref name="close"/> closes it.</summary>
    public static void Register(object owner, Action close)
    {
        Open[owner] = close;
        StartHook();
    }

    /// <summary>Like <see cref="Register"/>, but only one flyout is open at a time: others close.</summary>
    public static void RegisterFlyout(object owner, Action close)
    {
        foreach (var other in Flyouts.Where(f => f != owner).ToList())
        {
            if (Open.TryGetValue(other, out var closeOther))
                closeOther();
        }
        Flyouts.Add(owner);
        Register(owner, close);
    }

    public static void Unregister(object owner)
    {
        Flyouts.Remove(owner);
        if (Open.Remove(owner) && Open.Count == 0)
            StopHook();
    }

    private static void OnMouseDown(POINT point)
    {
        if (Open.Count == 0)
        {
            StopHook();
            return;
        }

        // Clicks on the bar or one of its menus/flyouts are WPF's to handle (e.g. a widget toggling its
        // own flyout). Those are all tool windows; Tempo's settings window is not, so it counts as outside.
        var root = GetAncestor(WindowFromPoint(point), GA_ROOT);
        NativeMethods.GetWindowThreadProcessId(root, out var pid);
        if (pid == Environment.ProcessId
            && ((long)NativeMethods.GetWindowLongPtr(root, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            return;

        foreach (var close in Open.Values.ToList())
        {
            try
            {
                close();
            }
            catch (Exception ex)
            {
                Log.Error("Could not close a popup", ex);
            }
        }
        Open.Clear();
        Flyouts.Clear();
        StopHook();
    }

    private static void StartHook()
    {
        lock (Gate)
        {
            if (_hookThread is not null)
                return;
            _hookThread = new Thread(HookThread) { IsBackground = true, Name = "Tempo outside-click hook" };
            _hookThread.Start();
        }
    }

    private static void StopHook()
    {
        lock (Gate)
        {
            if (_hookThread is null)
                return;
            if (_hookThreadId != 0)
                PostThreadMessage(_hookThreadId, WM_QUIT, 0, 0);
            _hookThread = null;
            _hookThreadId = 0;
        }
    }

    private static void HookThread()
    {
        _proc = Callback;
        var hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (hook == 0)
        {
            Log.Error($"Could not install the mouse hook (error {Marshal.GetLastWin32Error()}).");
            return;
        }

        // Create the message queue before publishing the thread id, so StopHook's WM_QUIT can't be lost.
        PeekMessage(out _, 0, 0, 0, 0);
        lock (Gate)
        {
            if (_hookThread != Thread.CurrentThread)
            {
                // Already stopped (or restarted) before this thread got going.
                UnhookWindowsHookEx(hook);
                return;
            }
            _hookThreadId = NativeMethods.GetCurrentThreadId();
        }

        try
        {
            // A low-level hook is serviced by this thread's message loop.
            while (GetMessage(out _, 0, 0, 0) > 0)
            {
            }
        }
        finally
        {
            UnhookWindowsHookEx(hook);
        }
    }

    private static nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (int)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN)
        {
            // MSLLHOOKSTRUCT starts with the cursor position in screen coordinates.
            var point = Marshal.PtrToStructure<POINT>(lParam);
            _dispatcher?.BeginInvoke(() => OnMouseDown(point));
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, nint hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG msg, nint hWnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, int msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
