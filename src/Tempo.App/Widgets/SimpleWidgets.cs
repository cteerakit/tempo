using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tempo.App.Shell;
using Tempo.Core.Settings;

namespace Tempo.App.Widgets;

internal sealed class NoSettings;

internal sealed partial class StartWidget(WidgetContext context) : WidgetViewModelBase(context)
{
    [RelayCommand]
    private void Open() => ShellActions.OpenStartMenu();
}

internal sealed class SpacerSettings
{
    [DisplayName("Width"), Range(2, 400)]
    public double Width { get; set; } = 16;
}

internal sealed class SpacerWidget(WidgetContext context, SpacerSettings settings) : WidgetViewModelBase(context)
{
    public double Width { get; } = settings.Width;
}

internal sealed class SeparatorWidget(WidgetContext context) : WidgetViewModelBase(context);

/// <summary>
/// Temporarily brings back the native Windows taskbar, mainly to reach notification-area icons,
/// which Tempo doesn't host yet.
/// </summary>
internal sealed partial class TaskbarToggleWidget : WidgetViewModelBase
{
    public TaskbarToggleWidget(WidgetContext context) : base(context)
    {
        Services.Settings.Changed += Refresh;
        Own(() => Services.Settings.Changed -= Refresh);
        Action notify = () => OnPropertyChanged(nameof(IsNativeTaskbarShown));
        Services.Taskbar.SuspendedChanged += notify;
        Own(() => Services.Taskbar.SuspendedChanged -= notify);
        Refresh();
    }

    public bool IsNativeTaskbarShown
    {
        get => Services.Taskbar.IsSuspended;
        set => Services.Taskbar.SetSuspended(value);
    }

    public override void Dispose()
    {
        if (Services.Taskbar.IsSuspended)
            Services.Taskbar.SetSuspended(false);
        base.Dispose();
    }

    private void Refresh() => IsVisible = Services.Settings.Current.Behavior.TaskbarMode != TaskbarMode.None;
}
