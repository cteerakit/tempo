# Tempo

Tempo is a WPF taskbar for Windows 10/11, built with .NET 10. It provides three widget zones, live appearance settings, and full-width or floating layouts.

## Install

Download `Tempo-win-Setup.exe` from the [latest release](https://github.com/cteerakit/tempo/releases/latest) and run it. Tempo installs for the current user in `%LOCALAPPDATA%\Tempo` without administrator rights and starts immediately. Windows SmartScreen may warn that the installer is unrecognized because it isn't code-signed; choose **More info > Run anyway**.

Installed copies update themselves. Tempo checks GitHub Releases 30 seconds after it starts and every 6 hours, and downloads new versions in the background. A downloaded update installs when Tempo exits, or at its next launch if Windows signed out first. To install it right away, choose **Restart to update** from the tray menu or from **Settings > Behavior > Updates**. Uninstall Tempo from **Settings > Apps**; the uninstaller closes Tempo, restores the Windows taskbar, and removes the sign-in startup entry.

## Release an update

Push a version tag:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

The `Release` workflow runs the tests, publishes a self-contained build, packs it with [Velopack](https://velopack.io) (installer, full package, and a delta from the previous release), and publishes a GitHub release. Tags must look like `v1.2.3`, and each release needs a higher version than the last. Installed copies find the release on their next check.

Updates are downloaded from this repository's public releases. Builds run from source (`dotnet run`) report that updates are only available in installed copies.

## Build and run

Install the .NET 10 SDK, then run from this directory:

```powershell
dotnet restore Tempo.sln
dotnet build Tempo.sln
dotnet test Tempo.sln --no-build
dotnet run --project src/Tempo.App --no-build
```

Launch Tempo again to open **Tempo settings**, or use its tray icon or the bar's context menu. In **Layout**, select a widget from the library and add it to a zone. Drag widgets between zones or use **Earlier** and **Later**. On the unlocked bar, grab a widget and drag it to reorder it or move it between zones. The widget follows the pointer while its neighbors animate into place; press Escape to cancel. Right-click the bar and choose **Lock bar** to prevent app and widget movement; choose **Unlock bar** to arrange them again. Appearance and behavior changes apply immediately.

To build while Tempo is running, use a separate output directory:

```powershell
dotnet build Tempo.sln -p:OutputPath=bin/Verify/
```

Use **Appearance > Blur strength** to blend from a clear background at 0% to full glass blur at 100%, independently of tint opacity. Changes apply while dragging, work in pill and full-width layouts, and are saved. **Reset** restores 100%; the slider is disabled when the backdrop is **None**. Run `dotnet run --project tests/Tempo.SliderCheck -- --blur-strength` to check continuous dragging and saved values with temporary settings.

## Pomodoro timer

Add **Pomodoro** from **Layout > Essentials**. Click the countdown to start, pause, reset the current session, or skip to the next one. Defaults are 25-minute focus sessions, 5-minute short breaks, and a 15-minute long break after four completed focus sessions. Durations, the long-break interval, and the completion sound are configurable in the widget settings. Each session waits for you to start it; completion opens the timer panel. The countdown catches up after the bar is hidden or the computer sleeps. Removing the widget, changing its settings, or restarting Tempo resets its timer.

## Pinned app sets

Add **Pinned app sets** from **Layout > Essentials**. Manage sets in **Settings > Pinned apps** to create an empty set or rename the selected set. Click the widget to switch sets; choosing a set closes the flyout. Right-click a running app and choose **Pin to Tempo** to add it to the active set; unpinning and dragging to reorder also affect only that set. Existing pins and Windows taskbar pins remain in **Default**. New sets contain only apps pinned in Tempo. Open windows stay visible in every set. Selection is shared across monitors and Apps widgets, while each Apps widget keeps its own pins and order. Sets and the selected set are saved across restarts.

Run `dotnet run --project tests/Tempo.SliderCheck -- --pin-sets` to verify the real switcher flyout, creation, renaming, duplicate names, pin/order isolation, and persistence with temporary settings.

## Restore the Windows taskbar

Use **Restore Windows taskbar** from Tempo's tray menu. If Tempo is unresponsive, run:

```powershell
& .\src\Tempo.App\bin\Debug\net10.0-windows10.0.22621.0\Tempo.exe --restore-taskbar
```

For an installed copy, run `& "$env:LOCALAPPDATA\Tempo\current\Tempo.exe" --restore-taskbar`. The command signals the running instance to exit and restores the native taskbar. After a forced termination, the next launch recovers the saved taskbar state before applying the configured behavior.

## Verify changes

Run `dotnet run --project tests/Tempo.SliderCheck -- --widget-drag` to verify pointer tracking, animated neighbors, cancellation, widget content without grips, reordering, moves into empty zones, lock/unlock, and persistence with temporary settings.

Core tests cover settings serialization, migration and normalization, atomic persistence, layout operations, window filtering, and CPU calculations.

With Tempo closed, run `dotnet run --project tests/Tempo.SliderCheck -- --pill` (or add `--secondary` for a secondary display). The check switches between full-width and pill layouts three times, verifies transparent corners, checks glass corner clipping, changes a window behind the pill from red to blue, and compares a striped background with blur enabled and disabled. It uses temporary settings. Glass in pill mode uses a live composition backdrop clipped to the same configured radius as the border and tint, including DPI scaling and the half-height limit. The check verifies square, small, medium, and maximum-radius outlines against screen pixels.

With Tempo closed, run `dotnet run --project tests/Tempo.SliderCheck -- --monitors` to check that all connected displays get a bar and switching back to primary leaves one bar.

With Tempo closed, run `dotnet run --project tests/Tempo.SliderCheck -- --height` and repeat with `--gap` to verify continuous dragging and placement after release. This check opens a temporary live bar and an offscreen Settings window without physical mouse input, then removes its bar and temporary settings.

With Tempo running and its settings window open, run the UI suite with the Tempo process ID:

```powershell
.\tests\ui\ui-tests.ps1 -AppPid (Get-Process Tempo).Id
```

The suite uses UI Automation patterns, saves screenshots and results under `tests/ui/out`, and restores the settings file afterward. It changes the live bar during the run; avoid editing Tempo settings concurrently.

Run `dotnet run --project tests/Tempo.SliderCheck -- --tray` to verify real notification-icon registration, the custom flyout, click callbacks, tooltip updates, removal, and taskbar listener exclusion. The check briefly registers a test tray icon and restores Explorer tray registration afterward.

Desktop integration still requires manual checks: maximized-window bounds at both screen edges, fullscreen behavior, Explorer restart, recovery after forced termination, app activation/grouping, media controls, and volume changes. Do not restart Explorer during unrelated desktop work.

## Files and limitations

Settings, logs, and saved taskbar recovery state live in `%APPDATA%\Tempo`. Settings reload after external edits. The solution contains `Tempo.Core` for settings and pure logic, `Tempo.App` for WPF and shell integration, and `Tempo.Core.Tests` for unit tests.

Choose **All monitors** in **Behavior > Monitor** to show Tempo on every connected display, or choose a single monitor. Changes apply immediately and Tempo adapts when displays are connected or disconnected. Add **Hidden icons** from **Layout > System** for Tempo's notification-icon flyout. It shows app tray icons, including those normally hidden by Windows, with live images and tooltips. Left-click or double-click to open an app, or right-click for its menu. The flyout uses the same appearance and placement as the network and volume panels and keeps the Windows taskbar hidden. A shared ManagedShell tray listener runs while at least one Hidden icons widget exists; removing the last widget or exiting Tempo releases it and asks apps to register with Explorer again. Shell integration uses explicit P/Invoke declarations in `Interop/NativeMethods.cs`.

Network opens a themed connection panel with active adapters and IPv4 addresses. Use **Manage Wi-Fi connections** to choose a network in Windows settings. Open the custom notification panel from the separate **Notifications** widget beside the clock in the default layout. For existing layouts, add **Notifications** in Layout. The panel supports notification access, recent notifications, and individual dismissal. Windows must grant notification listener access; if unavailable for this installation, the panel explains the issue and provides **Open Windows notifications**.

Run `dotnet run --project tests/Tempo.SliderCheck -- --flyouts` to check the network panel, independent calendar and notification widgets, exclusive popup behavior, and notification fallback without modifying your saved layout.

