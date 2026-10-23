using Tempo.App.Interop;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Shell;

internal sealed record MonitorInfo(nint Handle, string DeviceName, RECT Bounds, RECT WorkArea, bool IsPrimary, double Scale)
{
    public string DisplayName => $"{DeviceName.Replace(@"\\.\", "")} — {Bounds.Width}×{Bounds.Height}{(IsPrimary ? " (primary)" : "")}";
}

internal static class Monitors
{
    public static IReadOnlyList<MonitorInfo> GetAll()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(0, 0, (nint handle, nint _, ref RECT _, nint _) =>
        {
            if (TryGet(handle) is { } info)
                list.Add(info);
            return true;
        }, 0);
        return list;
    }

    /// <summary>The monitor with the given device name, falling back to the primary monitor.</summary>
    public static MonitorInfo Find(string? deviceName)
    {
        var all = GetAll();
        return all.FirstOrDefault(m => deviceName is not null && string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(m => m.IsPrimary)
               ?? all[0];
    }

    private static MonitorInfo? TryGet(nint handle)
    {
        var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(handle, ref info))
            return null;
        var scale = GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
        return new MonitorInfo(handle, info.szDevice, info.rcMonitor, info.rcWork, (info.dwFlags & MONITORINFOF_PRIMARY) != 0, scale);
    }
}
