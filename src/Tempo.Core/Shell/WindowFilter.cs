namespace Tempo.Core.Shell;

/// <summary>The facts about a top-level window needed to decide whether it belongs on a taskbar.</summary>
public readonly record struct WindowCandidate(
    bool IsVisible,
    bool IsCloaked,
    bool HasOwner,
    long ExStyle,
    string Title,
    string ClassName,
    bool IsOwnProcess);

/// <summary>Mirrors the rules Explorer uses to decide which windows get a taskbar button.</summary>
public static class WindowFilter
{
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_APPWINDOW = 0x00040000;
    public const long WS_EX_NOACTIVATE = 0x08000000;

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "Windows.UI.Core.CoreWindow",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow",
    };

    public static bool ShouldShow(in WindowCandidate w)
    {
        if (!w.IsVisible || w.IsCloaked)
            return false;
        if (string.IsNullOrWhiteSpace(w.Title) || ShellClasses.Contains(w.ClassName))
            return false;

        // Background helpers can set APPWINDOW as well as TOOLWINDOW (for example Raycast).
        if ((w.ExStyle & WS_EX_TOOLWINDOW) != 0)
            return false;

        // WS_EX_APPWINDOW forces a button for owned or non-activating app windows.
        if ((w.ExStyle & WS_EX_APPWINDOW) != 0)
            return true;
        if ((w.ExStyle & WS_EX_NOACTIVATE) != 0)
            return false;
        return !w.HasOwner;
    }
}

/// <summary>Decides which windows share a taskbar button and which pinned item they belong to.</summary>
public static class AppGrouping
{
    public const string ExplorerAumid = "Microsoft.Windows.Explorer";

    public static bool IsBrowserIdentity(string? aumid) =>
        aumid is not null && (aumid.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase)
            || aumid.StartsWith("Chromium", StringComparison.OrdinalIgnoreCase)
            || aumid.StartsWith("MSEdge", StringComparison.OrdinalIgnoreCase));

    public static bool IsBrowserWebApp(string? aumid) =>
        aumid?.Contains("._crx_", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// The AppUserModelID the shell uses for a window: its explicit id, or the implicit one Windows
    /// assigns. File Explorer windows carry no explicit id, yet its taskbar pin is keyed only by
    /// "Microsoft.Windows.Explorer" (the shortcut has no target path), so that id is filled in here.
    /// </summary>
    public static string? ResolveAumid(string? explicitAumid, string? exePath)
    {
        if (!string.IsNullOrWhiteSpace(explicitAumid))
            return explicitAumid;
        if (!string.IsNullOrWhiteSpace(exePath)
            && string.Equals(Path.GetFileName(exePath.Trim().Trim('"')), "explorer.exe", StringComparison.OrdinalIgnoreCase))
            return ExplorerAumid;
        return null;
    }

    /// <summary>Explicit AppUserModelIDs win over the executable path, matching Explorer's grouping.</summary>
    public static string GetGroupKey(string? appUserModelId, string? exePath, nint hwnd)
    {
        if (!string.IsNullOrWhiteSpace(appUserModelId))
            return "aumid:" + appUserModelId.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(exePath))
            return "exe:" + NormalizePath(exePath);
        return "hwnd:" + hwnd;
    }

    public static bool MatchesPin(string? pinAumid, string? pinTargetPath, string? windowAumid, string? windowExePath, string? pinArguments = null)
    {
        if (!string.IsNullOrWhiteSpace(pinAumid) && !string.IsNullOrWhiteSpace(windowAumid))
            return string.Equals(pinAumid.Trim(), windowAumid.Trim(), StringComparison.OrdinalIgnoreCase);

        // Browser profiles and web apps share an executable; only their identity can match a pin.
        if (IsBrowserIdentity(pinAumid) || IsBrowserIdentity(windowAumid)
            || IsBrowserWebApp(pinAumid) || IsBrowserWebApp(windowAumid))
            return false;

        if (string.IsNullOrWhiteSpace(pinTargetPath) || string.IsNullOrWhiteSpace(windowExePath))
            return false;
        return NormalizePath(pinTargetPath) == NormalizePath(windowExePath)
               || IsSquirrelLaunch(pinTargetPath, pinArguments, windowExePath);
    }

    /// <summary>
    /// Squirrel-installed apps (Discord, Slack, GitHub Desktop...) pin a launcher,
    /// <c>{root}\Update.exe --processStart App.exe</c>, while the window belongs to
    /// <c>{root}\app-1.2.3\App.exe</c>. Their shared AppUserModelID is set process-wide, which other
    /// processes can't read, so the launcher is recognised by its shape instead.
    /// </summary>
    internal static bool IsSquirrelLaunch(string pinTargetPath, string? pinArguments, string windowExePath)
    {
        var target = NormalizePath(pinTargetPath);
        if (Path.GetFileName(target) != "update.exe" || string.IsNullOrWhiteSpace(pinArguments))
            return false;

        var parts = pinArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var at = Array.FindIndex(parts, p => p.Equals("--processStart", StringComparison.OrdinalIgnoreCase));
        if (at < 0 || at + 1 >= parts.Length)
            return false;

        var exe = NormalizePath(windowExePath);
        var appDir = Path.GetDirectoryName(exe);
        return Path.GetFileName(exe) == parts[at + 1].Trim('"').ToLowerInvariant()
               && appDir is not null
               && Path.GetFileName(appDir).StartsWith("app-", StringComparison.Ordinal)
               && Path.GetDirectoryName(appDir) == Path.GetDirectoryName(target);
    }

    private static string NormalizePath(string path) =>
        Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')).Replace('/', '\\').ToLowerInvariant();
}
