using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tempo.App.Shell;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Tempo.App.Widgets;

internal sealed record NotificationItem(uint Id, string AppName, string Title, string Body, string Time);

internal sealed partial class NotificationsWidget(WidgetContext context) : WidgetViewModelBase(context)
{
    private bool _disposed;
    public ObservableCollection<NotificationItem> Items { get; } = [];

    [ObservableProperty]
    public partial string Message { get; set; } = "Allow notification access to see your notifications here.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasAccess { get; set; }

    public override void Start() => Every(TimeSpan.FromSeconds(5), () =>
    {
        if (IsFlyoutOpen) RefreshCommand.Execute(null);
    });

    protected override void OnFlyoutOpenChanged(bool isOpen)
    {
        if (isOpen) RefreshCommand.Execute(null);
    }

    [RelayCommand]
    private async Task EnableAccess()
    {
        try
        {
            await UserNotificationListener.Current.RequestAccessAsync();
            await Refresh();
        }
        catch (Exception ex) { Unavailable(ex); }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (IsBusy || _disposed) return;
        IsBusy = true;
        try
        {
            var listener = UserNotificationListener.Current;
            HasAccess = listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed;
            if (!HasAccess)
            {
                Items.Clear();
                Message = "Allow notification access to see your notifications here. If access was denied, enable it in Windows settings.";
                return;
            }
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            if (_disposed) return;
            // Access can be revoked while the query is in flight.
            HasAccess = listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed;
            Items.Clear();
            if (!HasAccess)
            {
                Message = "Notification access was turned off. Enable access in Windows settings.";
                return;
            }
            foreach (var notification in notifications.OrderByDescending(n => n.CreationTime))
            {
                var text = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?
                    .GetTextElements().Select(t => t.Text).ToArray() ?? [];
                Items.Add(new(notification.Id, notification.AppInfo.DisplayInfo.DisplayName,
                    text.FirstOrDefault() ?? "Notification", string.Join("\n", text.Skip(1)),
                    notification.CreationTime.ToLocalTime().ToString("g")));
            }
            Message = Items.Count == 0 ? "You're all caught up. New notifications will appear here." : "";
        }
        catch (Exception ex) { Unavailable(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task Dismiss(NotificationItem item)
    {
        try
        {
            var listener = UserNotificationListener.Current;
            if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed)
                listener.RemoveNotification(item.Id);
            await Refresh();
        }
        catch (Exception ex) { Unavailable(ex); }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        IsFlyoutOpen = false;
        ShellActions.Launch("ms-settings:privacy-notifications");
    }

    [RelayCommand]
    private void OpenWindowsNotifications()
    {
        IsFlyoutOpen = false;
        ShellActions.OpenNotificationCenter();
    }

    private void Unavailable(Exception ex)
    {
        Log.Error("Notification access failed", ex);
        Items.Clear();
        HasAccess = false;
        Message = "Windows couldn't provide notification access to Tempo. You can still open Windows notifications below.";
    }

    public override void Dispose()
    {
        _disposed = true;
        IsFlyoutOpen = false;
        base.Dispose();
    }
}
