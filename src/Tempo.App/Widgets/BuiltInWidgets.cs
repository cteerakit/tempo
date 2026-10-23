using Tempo.Core.Layout;

namespace Tempo.App.Widgets;

internal static class BuiltInWidgets
{
    public static void RegisterAll(WidgetRegistry registry)
    {
        registry.Register(WidgetDescriptor.Create<PomodoroSettings>(
            WidgetTypes.Pomodoro, "Pomodoro", "Focus timer with short and long breaks", "Essentials", "\uE916",
            (c, s) => new PomodoroWidget(c, s)));
        registry.Register(WidgetDescriptor.Create<NoSettings>(
            WidgetTypes.Start, "Start", "Opens the Start menu", "Essentials", "\uE8FC",
            (c, _) => new StartWidget(c)));
        registry.Register(WidgetDescriptor.Create<AppsSettings>(
            WidgetTypes.Apps, "Apps", "Pinned apps and open windows", "Essentials", "\uE71D",
            (c, s) => new AppsWidget(c, s)));
        registry.Register(WidgetDescriptor.Create<ClockSettings>(
            WidgetTypes.Clock, "Clock", "Time and date, with a calendar", "Essentials", "\uE787",
            (c, s) => new ClockWidget(c, s)));
        registry.Register(WidgetDescriptor.Create<NoSettings>(
            WidgetTypes.PinSetSwitcher, "Pinned app sets", "Switch between pinned app sets; manage sets in Settings > Pinned apps", "Essentials", "\uE8F1",
            (c, _) => new PinSetSwitcherWidget(c)));
        registry.Register(WidgetDescriptor.Create<MediaSettings>(
            WidgetTypes.Media, "Now playing", "Track info and playback controls for the current player", "Media", "\uEC4F",
            (c, s) => new MediaWidget(c, s)));
        registry.Register(WidgetDescriptor.Create<SoundSettings>(
            WidgetTypes.Sound, "Sound", "Volume, plus switching the output and input devices", "System", "\uE767",
            (c, s) => new SoundWidget(c, s)));
        registry.Register(WidgetDescriptor.Create<NetworkSettings>(
            WidgetTypes.Network, "Network", "Wi-Fi or Ethernet status", "System", "\uE701",
            (c, s) => new NetworkWidget(c, s)));
        registry.Register(WidgetDescriptor.Create<NoSettings>(
            WidgetTypes.Notifications, "Notifications", "Notification center", "System", "\uEA8F",
            (c, _) => new NotificationsWidget(c)));
        registry.Register(WidgetDescriptor.Create<SystemSettings>(
            WidgetTypes.System, "System stats", "CPU, memory and battery", "System", "\uE950",
            (c, s) => new SystemWidget(c, s)));
        registry.Register(WidgetDescriptor.Create<NoSettings>(
            WidgetTypes.Taskbar, "Windows taskbar", "Briefly shows the Windows taskbar, e.g. to reach tray icons", "System", "\uE70E",
            (c, _) => new TaskbarToggleWidget(c)));
        registry.Register(WidgetDescriptor.Create<NoSettings>(
            WidgetTypes.HiddenIcons, "Hidden icons", "App notification icons in a custom tray flyout", "System", "\uE70E",
            (c, _) => new HiddenIconsWidget(c)));
        registry.Register(WidgetDescriptor.Create<SpacerSettings>(
            WidgetTypes.Spacer, "Spacer", "Empty space", "Layout", "\uE75D",
            (c, s) => new SpacerWidget(c, s)));
        registry.Register(WidgetDescriptor.Create<NoSettings>(
            WidgetTypes.Separator, "Separator", "A thin divider line", "Layout", "\uE7FD",
            (c, _) => new SeparatorWidget(c)));
    }
}
