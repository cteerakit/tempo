using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using Tempo.App.Interop;
using Tempo.Core.Shell;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Shell;

internal sealed record TrackedWindow(nint Hwnd, string Title, string? ExePath, string? Aumid, bool IsPackaged, bool IsMinimized);

/// <summary>A pinned launcher: a taskbar .lnk, or a target the user pinned inside Tempo.</summary>
internal sealed record PinnedApp(string Id, string DisplayName, string LaunchPath, string? TargetPath, string? Aumid, string? Arguments = null);

/// <summary>Lists the windows that deserve a taskbar button and reports shell window events.</summary>
internal sealed class WindowTracker : IDisposable
{
    private readonly HwndSource _hookWindow;
    private readonly uint _shellHookMessage = RegisterWindowMessage("SHELLHOOK");
    private readonly Dictionary<uint, (string? Exe, string? Aumid)> _processCache = [];
    private readonly Dictionary<nint, long> _firstSeen = [];
    private long _sequence;
    private static Guid _propertyStoreIid = typeof(IPropertyStore).GUID;

    public WindowTracker()
    {
        // Shell hooks need a top-level window; this one is never shown.
        _hookWindow = new HwndSource(new HwndSourceParameters("Tempo.ShellHook")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, not visible
            ExtendedWindowStyle = WS_EX_TOOLWINDOW,
        });
        _hookWindow.AddHook(WndProc);
        RegisterShellHookWindow(_hookWindow.Handle);
    }

    /// <summary>A window was created, destroyed, retitled or activated.</summary>
    public event Action? WindowsChanged;

    /// <summary>A window is flashing to ask for attention.</summary>
    public event Action<nint>? WindowFlashed;

    /// <summary>Taskbar-worthy windows in the order they first appeared.</summary>
    public List<TrackedWindow> GetWindows()
    {
        _processCache.Clear();
        var handles = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            handles.Add(hwnd);
            return true;
        }, 0);

        var result = new List<TrackedWindow>();
        var alive = new HashSet<nint>();
        foreach (var hwnd in handles)
        {
            // GetWindowText sends WM_GETTEXT for our own windows. Never query helper windows:
            // a helper thread may be waiting for this UI thread during startup.
            if (!IsWindowVisible(hwnd) || IsCloaked(hwnd)) continue;
            var exStyle = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) continue;
            var candidate = new WindowCandidate(
                IsVisible: true,
                IsCloaked: false,
                HasOwner: GetWindow(hwnd, GW_OWNER) != 0,
                ExStyle: exStyle,
                Title: GetWindowTitle(hwnd),
                ClassName: GetWindowClass(hwnd),
                IsOwnProcess: false);
            if (!WindowFilter.ShouldShow(candidate))
                continue;

            alive.Add(hwnd);
            if (!_firstSeen.ContainsKey(hwnd))
                _firstSeen[hwnd] = ++_sequence;

            var (exe, aumid, packaged) = Identify(hwnd, candidate.ClassName);
            result.Add(new TrackedWindow(hwnd, candidate.Title, exe, aumid, packaged, IsIconic(hwnd)));
        }

        foreach (var stale in _firstSeen.Keys.Where(h => !alive.Contains(h)).ToList())
        {
            _firstSeen.Remove(stale);
        }

        result.Sort((a, b) => _firstSeen[a.Hwnd].CompareTo(_firstSeen[b.Hwnd]));
        return result;
    }

    private (string? Exe, string? Aumid, bool Packaged) Identify(nint hwnd, string className)
    {
        GetWindowThreadProcessId(hwnd, out var pid);

        // UWP apps live in a frame window owned by ApplicationFrameHost; the app itself owns a child CoreWindow.
        if (className == "ApplicationFrameWindow")
        {
            var corePid = FindCoreWindowProcess(hwnd);
            if (corePid != 0)
                pid = corePid;
        }

        var (exe, processAumid) = GetProcessInfo(pid);
        var windowAumid = GetWindowAumid(hwnd);
        return (exe, AppGrouping.ResolveAumid(windowAumid ?? processAumid, exe), processAumid is not null);
    }

    private static uint FindCoreWindowProcess(nint frame)
    {
        GetWindowThreadProcessId(frame, out var framePid);
        uint found = 0;
        EnumChildWindows(frame, (child, _) =>
        {
            if (GetWindowClass(child) == "Windows.UI.Core.CoreWindow")
            {
                GetWindowThreadProcessId(child, out var pid);
                if (pid != framePid)
                {
                    found = pid;
                    return false;
                }
            }
            return true;
        }, 0);
        return found;
    }

    private (string? Exe, string? Aumid) GetProcessInfo(uint pid)
    {
        if (_processCache.TryGetValue(pid, out var info))
            return info;

        string? exe = null, aumid = null;
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process != 0)
        {
            try
            {
                var size = 1024;
                var path = new StringBuilder(size);
                if (QueryFullProcessImageName(process, 0, path, ref size))
                    exe = path.ToString();

                var length = 0;
                GetApplicationUserModelId(process, ref length, null);
                if (length > 0)
                {
                    var id = new StringBuilder(length);
                    if (GetApplicationUserModelId(process, ref length, id) == 0)
                        aumid = id.ToString();
                }
            }
            finally
            {
                CloseHandle(process);
            }
        }

        // Process ids are reused, so keep the cache from growing forever.
        if (_processCache.Count > 512)
            _processCache.Clear();
        return _processCache[pid] = (exe, aumid);
    }

    /// <summary>An explicit AppUserModelID set on the window (Chrome profiles, Terminal, VS Code...).</summary>
    private string? GetWindowAumid(nint hwnd) => ReadWindowProperty(hwnd, PROPERTYKEY.AppUserModel_ID);

    internal static string? ReadWindowProperty(nint hwnd, PROPERTYKEY key)
    {
        string? aumid = null;
        if (SHGetPropertyStoreForWindow(hwnd, ref _propertyStoreIid, out var store) == 0 && store is not null)
        {
            try
            {
                if (store.GetValue(ref key, out var value) == 0)
                {
                    aumid = value.GetString();
                    PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        return string.IsNullOrWhiteSpace(aumid) ? null : aumid;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != _shellHookMessage)
            return 0;

        switch ((int)wParam)
        {
            case HSHELL_FLASH:
                WindowFlashed?.Invoke(lParam);
                break;
            case HSHELL_WINDOWCREATED or HSHELL_WINDOWDESTROYED or HSHELL_WINDOWACTIVATED
                or HSHELL_RUDEAPPACTIVATED or HSHELL_REDRAW or HSHELL_WINDOWREPLACED:
                WindowsChanged?.Invoke();
                break;
        }
        return 0;
    }

    public void Dispose()
    {
        DeregisterShellHookWindow(_hookWindow.Handle);
        _hookWindow.RemoveHook(WndProc);
        _hookWindow.Dispose();
    }
}

internal static class PinnedApps
{
    private static readonly Dictionary<string, string> AppShortcuts = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _shortcutsScannedAt;
    private static readonly Dictionary<string, (string Command, string? Icon, string Path)> ProfileLaunchers = new(StringComparer.OrdinalIgnoreCase);

    public static string? FindProfileShortcut(string aumid, nint hwnd)
    {
        var installed = FindAppShortcut(aumid);
        if (installed is not null) return installed;
        var command = WindowTracker.ReadWindowProperty(hwnd, PROPERTYKEY.AppUserModel_RelaunchCommand)?.Trim();
        if (string.IsNullOrEmpty(command)) return null;
        var iconResource = WindowTracker.ReadWindowProperty(hwnd, PROPERTYKEY.AppUserModel_RelaunchIconResource);
        if (ProfileLaunchers.TryGetValue(aumid, out var cached) && cached.Command == command
            && cached.Icon == iconResource && File.Exists(cached.Path))
            return cached.Path;

        var quoted = command[0] == '"';
        var end = command.IndexOf(quoted ? '"' : ' ', 1);
        if (quoted && end < 0) return null;
        if (end < 0) end = command.Length;
        var exe = command[(quoted ? 1 : 0)..end];
        var arguments = command[Math.Min(end + 1, command.Length)..].TrimStart();
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tempo", "Launchers");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(aumid)));
        directory = Path.Combine(directory, hash);
        var path = Path.Combine(directory, Path.GetFileNameWithoutExtension(exe) + ".lnk");
        object? link = null;
        try
        {
            Directory.CreateDirectory(directory);
            link = new ShellLinkCoClass();
            Marshal.ThrowExceptionForHR(((IShellLinkW)link).SetPath(exe));
            Marshal.ThrowExceptionForHR(((IShellLinkW)link).SetArguments(arguments));
            if (IconResource.TryParse(iconResource, out var icon))
                Marshal.ThrowExceptionForHR(((IShellLinkW)link).SetIconLocation(icon.Path, icon.Index));
            var key = PROPERTYKEY.AppUserModel_ID;
            var value = new PROPVARIANT { vt = PROPVARIANT.VT_LPWSTR, pointerValue = Marshal.StringToCoTaskMemUni(aumid) };
            try { Marshal.ThrowExceptionForHR(((IPropertyStore)link).SetValue(ref key, ref value)); }
            finally { PropVariantClear(ref value); }
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path, true);
            ProfileLaunchers[aumid] = (command, iconResource, path);
            return path;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not preserve browser profile launcher", ex);
            return null;
        }
        finally { if (link is not null) Marshal.ReleaseComObject(link); }
    }

    public static string? FindWebAppShortcut(string aumid)
    {
        return AppGrouping.IsBrowserWebApp(aumid) ? FindAppShortcut(aumid) : null;
    }

    public static string? ReadShortcutIconResource(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase)) return null;
        object? link = null;
        try
        {
            link = new ShellLinkCoClass();
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(path, 0);
            var icon = new StringBuilder(32768);
            if (((IShellLinkW)link).GetIconLocation(icon, icon.Capacity, out var index) == 0 && icon.Length > 0)
                return $"{icon},{index}";
            return null;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException) { return null; }
        finally { if (link is not null) Marshal.ReleaseComObject(link); }
    }

    public static string? FindAppShortcut(string aumid)
    {
        if (DateTime.UtcNow - _shortcutsScannedAt > TimeSpan.FromSeconds(30))
        {
            AppShortcuts.Clear();
            foreach (var root in new[] { TaskbarPinFolder,
                         Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var path in Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
                         { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                {
                    var (_, _, id) = ReadShortcut(path);
                    if (!string.IsNullOrWhiteSpace(id))
                        AppShortcuts.TryAdd(id!, path);
                }
            }
            _shortcutsScannedAt = DateTime.UtcNow;
        }
        return AppShortcuts.TryGetValue(aumid, out var shortcut) && File.Exists(shortcut) ? shortcut : null;
    }

    public static string TaskbarPinFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    /// <summary>Shortcuts pinned to the Windows taskbar, oldest first (an approximation of pin order).</summary>
    public static List<PinnedApp> LoadTaskbarPins()
    {
        if (!Directory.Exists(TaskbarPinFolder))
            return [];

        return new DirectoryInfo(TaskbarPinFolder)
            .EnumerateFiles("*.lnk")
            .OrderBy(f => f.CreationTimeUtc)
            .Select(f =>
            {
                var (target, arguments, aumid) = ReadShortcut(f.FullName);
                return new PinnedApp(f.FullName, Path.GetFileNameWithoutExtension(f.Name), f.FullName, target, aumid, arguments);
            })
            .ToList();
    }

    /// <summary>A target pinned in Tempo: an executable path or <c>shell:AppsFolder\{AUMID}</c>.</summary>
    public static PinnedApp FromTarget(string target)
    {
        const string appsFolder = @"shell:AppsFolder\";
        if (target.StartsWith(appsFolder, StringComparison.OrdinalIgnoreCase))
        {
            var aumid = target[appsFolder.Length..];
            return new PinnedApp(target, aumid.Split('!')[0].Split('_')[0], target, null, aumid);
        }
        if (string.Equals(Path.GetExtension(target), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var (exe, arguments, aumid) = ReadShortcut(target);
            return new PinnedApp(target, Path.GetFileNameWithoutExtension(target), target, exe, aumid, arguments);
        }
        var name = Path.GetFileNameWithoutExtension(target);
        try
        {
            var description = System.Diagnostics.FileVersionInfo.GetVersionInfo(target).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
                name = description;
        }
        catch (FileNotFoundException)
        {
        }
        return new PinnedApp(target, name, target, target, null);
    }

    private static (string? Target, string? Arguments, string? Aumid) ReadShortcut(string path)
    {
        object? link = null;
        try
        {
            link = new ShellLinkCoClass();
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(path, 0);

            var target = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(target, target.Capacity, 0, 0);
            var arguments = new StringBuilder(2048);
            ((IShellLinkW)link).GetArguments(arguments, arguments.Capacity);

            string? aumid = null;
            var key = PROPERTYKEY.AppUserModel_ID;
            if (((IPropertyStore)link).GetValue(ref key, out var value) == 0)
            {
                aumid = value.GetString();
                PropVariantClear(ref value);
            }
            return (target.Length > 0 ? target.ToString() : null,
                    arguments.Length > 0 ? arguments.ToString() : null,
                    string.IsNullOrWhiteSpace(aumid) ? null : aumid);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or FileNotFoundException)
        {
            return (null, null, null);
        }
        finally
        {
            if (link is not null)
                Marshal.ReleaseComObject(link);
        }
    }
}
