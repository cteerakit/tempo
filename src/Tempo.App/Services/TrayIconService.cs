using ManagedShell.WindowsTray;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Services;

internal sealed class TrayIconService : IDisposable
{
    private TrayService? _listener;
    private NotificationArea? _area;
    private ManagedShell.AppBar.AppBarManager? _appBars;
    private TraySubclass? _subclass;
    private int _users;
    private readonly DispatcherTimer _cleanup = new() { Interval = TimeSpan.FromSeconds(2) };

    public TrayIconService()
    {
        _cleanup.Tick += (_, _) =>
        {
            if (_area is null) return;
            foreach (var icon in _area.TrayIcons.Where(icon => !IsWindow(icon.HWnd)).ToArray())
                _area.TrayIcons.Remove(icon);
        };
    }

    public NotificationArea GetArea()
    {
        if (_area is not null)
        {
            _users++;
            return _area;
        }
        _listener = new TrayService();
        _area = new NotificationArea([], _listener, new ExplorerTrayService());
        // The tray listener also intercepts app-bar IPC; forward its shared position data to Explorer.
        _appBars = new ManagedShell.AppBar.AppBarManager(new ManagedShell.AppBar.ExplorerHelper(_area));
        _area.Initialize();
        if (_area.IsFailed || _area.Handle == 0)
        {
            _listener.Dispose();
            _appBars.Dispose();
            _appBars = null;
            _area = null;
            _listener = null;
            throw new InvalidOperationException("The notification icon listener could not start.");
        }
        _subclass = ProcessTrayMessage;
        if (!SetWindowSubclass(_area.Handle, _subclass, 1, 0))
        {
            Dispose();
            throw new InvalidOperationException("The notification icon listener could not be attached.");
        }
        _cleanup.Start();
        _users = 1;
        return _area;
    }

    public void Release()
    {
        if (_users > 0 && --_users == 0)
            Dispose();
    }

    private nint ProcessTrayMessage(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        // ManagedShell 0.0.374 drops the GUID when matching NIM_DELETE. Match the original
        // identifier here so GUID-only deletions don't leave an icon behind in the flyout.
        if (message == 0x004A && lParam != 0 && _area is not null)
        {
            var copy = Marshal.PtrToStructure<ManagedShell.Interop.NativeMethods.COPYDATASTRUCT>(lParam);
            if (copy.dwData == 1 && copy.lpData != 0
                && copy.cbData >= Marshal.SizeOf<ManagedShell.Interop.NativeMethods.SHELLTRAYDATA>())
            {
                var tray = Marshal.PtrToStructure<ManagedShell.Interop.NativeMethods.SHELLTRAYDATA>(copy.lpData);
                if (tray.dwMessage == 2)
                {
                    var icon = _area.TrayIcons.FirstOrDefault(icon => tray.nid.guidItem != Guid.Empty
                        ? icon.GUID == tray.nid.guidItem
                        : icon.HWnd == tray.nid.hWnd && icon.UID == tray.nid.uID);
                    if (icon is not null)
                    {
                        _area.TrayIcons.Remove(icon);
                        return 1;
                    }
                }
            }
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        _cleanup.Stop();
        if (_area is not null && _subclass is not null)
            RemoveWindowSubclass(_area.Handle, _subclass, 1);
        _area?.Dispose();
        _appBars?.Dispose();
        _appBars = null;
        _area?.TrayIcons.Clear();
        _area = null;
        _listener = null;
        _subclass = null;
        _users = 0;
    }
}
