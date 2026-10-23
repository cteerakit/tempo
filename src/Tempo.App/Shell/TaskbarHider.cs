using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tempo.Core.Settings;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Shell;

/// <summary>
/// Auto-hides or hides the Windows taskbar while Tempo runs, and guarantees it comes back.
/// Before the first change, the original taskbar state is written to disk; any later launch
/// (including after a crash or a hard kill) restores from that file.
/// </summary>
internal sealed class TaskbarHider(string statePath)
{
    private sealed record SavedState(int OriginalAppBarState);

    private readonly object _gate = new();
    private TaskbarMode _mode = TaskbarMode.None;
    private bool _suspended;

    public TaskbarMode Mode => _mode;

    /// <summary>When true the native taskbar is shown temporarily regardless of <see cref="Mode"/>.</summary>
    public bool IsSuspended => _suspended;

    private TaskbarMode Effective => _suspended ? TaskbarMode.None : _mode;

    /// <summary>Restores a taskbar left hidden by a previous run that didn't exit cleanly.</summary>
    public void RecoverFromPreviousRun()
    {
        if (File.Exists(statePath))
        {
            Log.Info("Restoring taskbar state left by a previous session.");
            Restore();
        }
    }

    public void Apply(TaskbarMode mode)
    {
        _mode = mode;
        Enforce();
    }

    public void SetSuspended(bool suspended)
    {
        if (_suspended == suspended)
            return;
        _suspended = suspended;
        Enforce();
        SuspendedChanged?.Invoke();
    }

    public event Action? SuspendedChanged;

    /// <summary>
    /// Called periodically: Explorer re-shows its taskbar on some events (Start menu, Explorer restart).
    /// </summary>
    public void Reassert()
    {
        if (Effective == TaskbarMode.Hidden && GetTrayWindows().Any(IsWindowVisible))
            Enforce();
    }

    /// <summary>Puts the taskbar back exactly as it was. Safe to call from any thread, repeatedly.</summary>
    public void Restore()
    {
        lock (_gate)
        {
            var saved = ReadState();
            foreach (var tray in GetTrayWindows())
                ShowWindow(tray, SW_SHOWNA);
            if (saved is not null)
                SetAppBarState(saved.OriginalAppBarState);
            TryDelete();
        }
    }

    private void Enforce()
    {
        lock (_gate)
        {
            var effective = Effective;
            if (effective == TaskbarMode.None)
            {
                Restore();
                return;
            }

            if (!File.Exists(statePath))
                WriteState(new SavedState(GetAppBarState()));

            SetAppBarState(GetAppBarState() | ABS_AUTOHIDE);
            foreach (var tray in GetTrayWindows())
                ShowWindow(tray, effective == TaskbarMode.Hidden ? SW_HIDE : SW_SHOWNA);
        }
    }

    public static IEnumerable<nint> GetTrayWindows()
    {
        var windows = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId != Environment.ProcessId
                && GetWindowClass(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                windows.Add(hwnd);
            return true;
        }, 0);
        foreach (var hwnd in windows)
            yield return hwnd;
    }

    private static nint GetNativePrimaryTaskbar() =>
        GetTrayWindows().FirstOrDefault(hwnd => GetWindowClass(hwnd) == "Shell_TrayWnd");

    private static int GetAppBarState()
    {
        var data = new Interop.APPBARDATA { cbSize = Marshal.SizeOf<Interop.APPBARDATA>(), hWnd = GetNativePrimaryTaskbar() };
        return (int)SHAppBarMessage(ABM_GETSTATE, ref data);
    }

    private static void SetAppBarState(int state)
    {
        var data = new Interop.APPBARDATA { cbSize = Marshal.SizeOf<Interop.APPBARDATA>(), hWnd = GetNativePrimaryTaskbar(), lParam = state };
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    private SavedState? ReadState()
    {
        try
        {
            return File.Exists(statePath) ? JsonSerializer.Deserialize<SavedState>(File.ReadAllText(statePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Error("Could not read saved taskbar state", ex);
            // Unknown original state: "always on top, not auto-hidden" is the Windows default.
            return new SavedState(ABS_ALWAYSONTOP);
        }
    }

    private void WriteState(SavedState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, JsonSerializer.Serialize(state));
    }

    private void TryDelete()
    {
        try
        {
            File.Delete(statePath);
        }
        catch (IOException ex)
        {
            Log.Error("Could not delete taskbar state file", ex);
        }
    }
}
