using System.Runtime.InteropServices;
using System.Windows.Interop;
using Tempo.App.Interop;
using Tempo.Core.Settings;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Shell;

/// <summary>
/// Registers a window as a shell app bar so Windows keeps maximized windows out of the space it reserves,
/// and relays the shell's position and full-screen notifications.
/// </summary>
internal sealed class AppBarService : IDisposable
{
    private readonly nint _hwnd;
    private readonly HwndSource _source;
    private readonly uint _callbackMessage = RegisterWindowMessage("Tempo.AppBarCallback");
    private bool _registered;
    private RECT? _reserved;

    public AppBarService(nint hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source.AddHook(WndProc);
    }

    /// <summary>Another app bar or the taskbar moved; the bar should recompute its position.</summary>
    public event Action? PositionChanged;

    /// <summary>A full-screen app opened (true) or closed (false) on this bar's monitor.</summary>
    public event Action<bool>? FullscreenChanged;

    public void EnsureRegistered()
    {
        if (_registered)
            return;
        var data = NewData();
        data.uCallbackMessage = _callbackMessage;
        _registered = SHAppBarMessage(ABM_NEW, ref data) != 0;
        _reserved = null;
    }

    /// <summary>
    /// Reserves <paramref name="thickness"/> pixels along <paramref name="edge"/> of <paramref name="monitor"/>.
    /// Returns the rectangle the shell granted, which may be shifted past other app bars.
    /// </summary>
    public RECT Reserve(BarEdge edge, RECT monitor, int thickness)
    {
        EnsureRegistered();

        var data = NewData();
        data.uEdge = edge == BarEdge.Top ? ABE_TOP : ABE_BOTTOM;
        data.rc = monitor;
        ApplyThickness(ref data, edge, thickness);
        SHAppBarMessage(ABM_QUERYPOS, ref data);
        ApplyThickness(ref data, edge, thickness);

        if (_reserved is not { } current || !current.Equals(data.rc))
        {
            SHAppBarMessage(ABM_SETPOS, ref data);
            _reserved = data.rc;
        }
        return data.rc;
    }

    /// <summary>Stops reserving space but stays registered, so full-screen notifications keep arriving.</summary>
    public void ReleaseReservation()
    {
        if (_reserved is null)
            return;
        Unregister();
        EnsureRegistered();
    }

    /// <summary>Explorer forgets app bars when it restarts.</summary>
    public void ReRegisterAfterExplorerRestart()
    {
        Unregister();
        EnsureRegistered();
    }

    public void Unregister()
    {
        if (!_registered)
            return;
        var data = NewData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        _registered = false;
        _reserved = null;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
    }

    private static void ApplyThickness(ref APPBARDATA data, BarEdge edge, int thickness)
    {
        if (edge == BarEdge.Top)
            data.rc.Bottom = data.rc.Top + thickness;
        else
            data.rc.Top = data.rc.Bottom - thickness;
    }

    private APPBARDATA NewData() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == _callbackMessage)
        {
            switch ((int)wParam)
            {
                case ABN_POSCHANGED:
                    PositionChanged?.Invoke();
                    break;
                case ABN_FULLSCREENAPP:
                    FullscreenChanged?.Invoke(lParam != 0);
                    break;
            }
            handled = true;
        }
        else if (_registered && msg is WM_ACTIVATE or WM_WINDOWPOSCHANGED)
        {
            var data = NewData();
            SHAppBarMessage(msg == WM_ACTIVATE ? ABM_ACTIVATE : ABM_WINDOWPOSCHANGED, ref data);
        }
        return 0;
    }
}
