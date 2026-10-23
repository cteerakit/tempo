using Tempo.Core.Diagnostics;
using Tempo.Core.Shell;
using static Tempo.Core.Shell.WindowFilter;

namespace Tempo.Core.Tests;

public class ShellLogicTests
{
    private static WindowCandidate Normal(string title = "Document - Notepad") =>
        new(IsVisible: true, IsCloaked: false, HasOwner: false, ExStyle: 0, Title: title, ClassName: "Notepad", IsOwnProcess: false);

    [Fact]
    public void ShowsOrdinaryAppWindow() => Assert.True(ShouldShow(Normal()));

    [Fact]
    public void HidesInvisibleCloakedAndUntitledWindows()
    {
        Assert.False(ShouldShow(Normal() with { IsVisible = false }));
        Assert.False(ShouldShow(Normal() with { IsCloaked = true }));
        Assert.False(ShouldShow(Normal(title: "  ")));
        Assert.True(ShouldShow(Normal("Tempo settings") with { IsOwnProcess = true }));
    }

    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Progman")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    public void HidesShellWindows(string className) =>
        Assert.False(ShouldShow(Normal() with { ClassName = className }));

    [Fact]
    public void HidesToolOwnedAndNoActivateWindows()
    {
        Assert.False(ShouldShow(Normal() with { ExStyle = WS_EX_TOOLWINDOW }));
        Assert.False(ShouldShow(Normal() with { ExStyle = WS_EX_NOACTIVATE }));
        Assert.False(ShouldShow(Normal() with { HasOwner = true }));
    }

    [Fact]
    public void AppWindowStyleOverridesOwnerAndNoActivate() =>
        Assert.True(ShouldShow(Normal() with { HasOwner = true, ExStyle = WS_EX_APPWINDOW | WS_EX_NOACTIVATE }));

    [Theory]
    [InlineData(WS_EX_APPWINDOW | WS_EX_TOOLWINDOW)]
    [InlineData(0x080C0080)] // Raycast's visible background helper window.
    public void HidesToolWindowsEvenWithAppWindowStyle(long exStyle) =>
        Assert.False(ShouldShow(Normal("Raycast") with { ExStyle = exStyle }));

    [Fact]
    public void GroupKey_PrefersAumidThenExeThenHwnd()
    {
        Assert.Equal("aumid:microsoft.windowsterminal", AppGrouping.GetGroupKey(" Microsoft.WindowsTerminal ", @"C:\x.exe", 1));
        Assert.Equal(@"exe:c:\apps\x.exe", AppGrouping.GetGroupKey(null, @"C:/Apps/X.exe", 1));
        Assert.Equal("hwnd:42", AppGrouping.GetGroupKey("", null, 42));
    }

    [Fact]
    public void PinMatching()
    {
        // Explicit ids on both sides decide on their own.
        Assert.True(AppGrouping.MatchesPin("Chrome", @"C:\chrome.exe", "chrome", @"D:\other.exe"));
        Assert.False(AppGrouping.MatchesPin("Chrome.Profile1", @"C:\chrome.exe", "Chrome.Profile2", @"C:\chrome.exe"));

        // Otherwise fall back to the executable path.
        Assert.True(AppGrouping.MatchesPin(null, "\"C:\\Windows\\notepad.exe\"", "Some.Id", @"c:\windows\NOTEPAD.EXE"));
        Assert.False(AppGrouping.MatchesPin(null, null, null, @"C:\a.exe"));
    }

    [Theory]
    [InlineData("Chrome.Profile.1")]
    [InlineData("MSEdge.Profile.1")]
    [InlineData("Chromium.Profile.1")]
    public void BrowserProfiles_RequireMatchingPinIdentity(string profile)
    {
        const string exe = @"C:\Apps\chrome.exe";
        Assert.False(AppGrouping.MatchesPin(null, exe, profile, exe));
        Assert.False(AppGrouping.MatchesPin(profile, exe, null, exe));
        Assert.True(AppGrouping.MatchesPin(profile, exe, profile, exe));
        Assert.NotEqual(AppGrouping.GetGroupKey(profile, exe, 1), AppGrouping.GetGroupKey("Chrome.Other", exe, 2));
    }

    [Fact]
    public void BrowserWebApps_DoNotJoinBrowserPinsByExecutable()
    {
        const string chrome = @"C:\Apps\chrome.exe";
        const string webApp = "Chrome._crx_abcdefghijklmnop";
        Assert.False(AppGrouping.MatchesPin(null, chrome, webApp, chrome));
        Assert.False(AppGrouping.MatchesPin(webApp, chrome, null, chrome, "--app-id=abcdefghijklmnop"));
        Assert.True(AppGrouping.MatchesPin(webApp, chrome, webApp, chrome));
        Assert.NotEqual(AppGrouping.GetGroupKey(webApp, chrome, 1), AppGrouping.GetGroupKey(null, chrome, 2));
    }

    [Fact]
    public void ExplorerWindows_GetTheShellIdAndJoinThePinnedFileExplorer()
    {
        var aumid = AppGrouping.ResolveAumid(null, @"C:\WINDOWS\Explorer.EXE");

        Assert.Equal(AppGrouping.ExplorerAumid, aumid);
        // The "File Explorer" taskbar pin has an AUMID but no target path.
        Assert.True(AppGrouping.MatchesPin(AppGrouping.ExplorerAumid, null, aumid, @"C:\WINDOWS\Explorer.EXE"));
    }

    [Fact]
    public void ResolveAumid_KeepsExplicitIdsAndLeavesOtherAppsAlone()
    {
        Assert.Equal("Custom.Id", AppGrouping.ResolveAumid("Custom.Id", @"C:\Windows\explorer.exe"));
        Assert.Null(AppGrouping.ResolveAumid(null, @"C:\Windows\notepad.exe"));
        Assert.Null(AppGrouping.ResolveAumid(null, null));
    }

    [Fact]
    public void SquirrelLauncherPin_MatchesTheVersionedAppExecutable()
    {
        const string pin = @"C:\Users\me\AppData\Local\Discord\Update.exe";
        const string args = "--processStart Discord.exe";

        Assert.True(AppGrouping.MatchesPin("com.squirrel.Discord.Discord", pin, null,
            @"C:\Users\me\AppData\Local\Discord\app-1.0.9264\Discord.exe", args));
        // Same launcher shape, but a different app, no version folder, another install root, or no args.
        Assert.False(AppGrouping.MatchesPin(null, pin, null, @"C:\Users\me\AppData\Local\Discord\app-1.0.9264\Other.exe", args));
        Assert.False(AppGrouping.MatchesPin(null, pin, null, @"C:\Users\me\AppData\Local\Discord\Discord.exe", args));
        Assert.False(AppGrouping.MatchesPin(null, pin, null, @"C:\Users\me\AppData\Local\Slack\app-4.0.0\Discord.exe", args));
        Assert.False(AppGrouping.MatchesPin(null, pin, null, @"C:\Users\me\AppData\Local\Discord\app-1.0.9264\Discord.exe", null));
    }

    [Fact]
    public void CpuUsage_FromSampleDeltas()
    {
        var cpu = new CpuUsageCalculator();

        Assert.Null(cpu.Next(idle: 100, kernel: 200, user: 100));
        // 100 busy-or-idle ticks of kernel (75 idle) + 100 user ticks → 125 busy of 200.
        Assert.Equal(62.5, cpu.Next(idle: 175, kernel: 300, user: 200));
        Assert.Equal(0, cpu.Next(idle: 175, kernel: 300, user: 200));
        // Counters going backwards (e.g. wrap) resets instead of producing garbage.
        Assert.Null(cpu.Next(idle: 1, kernel: 1, user: 1));
    }
}
