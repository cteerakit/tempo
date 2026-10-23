using System.Diagnostics;
using Microsoft.Win32;
using Tempo.App.Interop;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Shell;

internal static class ShellActions
{
    public static void OpenStartMenu() =>
        SendKeys((VK_LWIN, false), (VK_LWIN, true));

    /// <summary>Win+A: quick settings (Wi-Fi, volume, brightness...).</summary>
    public static void OpenQuickSettings() =>
        SendKeys((VK_LWIN, false), (0x41, false), (0x41, true), (VK_LWIN, true));

    /// <summary>Win+N: notification centre and calendar.</summary>
    public static void OpenNotificationCenter() =>
        SendKeys((VK_LWIN, false), (0x4E, false), (0x4E, true), (VK_LWIN, true));

    public static void OpenSoundSettings() => Launch("ms-settings:sound");

    public static void Launch(string target, string? arguments = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = arguments ?? "" })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error($"Could not launch {target}", ex);
        }
    }

    /// <summary>Brings a window to the front, working around the foreground-lock rules.</summary>
    public static void Activate(nint hwnd)
    {
        if (IsIconic(hwnd))
            ShowWindowAsync(hwnd, SW_RESTORE);

        if (SetForegroundWindow(hwnd))
            return;

        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var ourThread = GetCurrentThreadId();
        var attached = foregroundThread != ourThread && AttachThreadInput(ourThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            if (SetForegroundWindow(hwnd))
                return;
        }
        finally
        {
            if (attached)
                AttachThreadInput(ourThread, foregroundThread, false);
        }

        // Last resort: a synthetic Alt press makes Windows treat us as the source of recent input.
        SendKeys((VK_MENU, false), (VK_MENU, true));
        SetForegroundWindow(hwnd);
    }

    public static void Minimize(nint hwnd) => ShowWindowAsync(hwnd, SW_MINIMIZE);

    public static void Close(nint hwnd) => PostMessage(hwnd, WM_CLOSE, 0, 0);
}

/// <summary>Start-with-Windows via the per-user Run key.</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Tempo";

    public static void Sync(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            var command = $"\"{Environment.ProcessPath}\"";
            if (enabled)
            {
                if (key.GetValue(ValueName) as string != command)
                    key.SetValue(ValueName, command);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not update the startup registration", ex);
        }
    }
}
