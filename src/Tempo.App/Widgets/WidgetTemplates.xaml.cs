using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tempo.App.Controls;

namespace Tempo.App.Widgets;

public partial class WidgetTemplates : ResourceDictionary
{
    public WidgetTemplates() => InitializeComponent();

    private static uint TrayPosition(FrameworkElement element, Point point)
    {
        var screen = element.PointToScreen(point);
        if (element.DataContext is ManagedShell.WindowsTray.NotifyIcon icon)
        {
            var origin = element.PointToScreen(new Point());
            var end = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
            icon.Placement = new ManagedShell.Interop.NativeMethods.Rect
            {
                Left = (int)origin.X, Top = (int)origin.Y,
                Right = (int)end.X, Bottom = (int)end.Y,
            };
        }
        return unchecked((uint)((ushort)(int)screen.X | ((uint)(ushort)(int)screen.Y << 16)));
    }

    private void TrayIcon_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ManagedShell.WindowsTray.NotifyIcon icon } element)
            return;
        var doubleClickTime = e.ClickCount == 2 ? (int)Tempo.App.Interop.NativeMethods.GetDoubleClickTime() : -1;
        icon.IconMouseDown(e.ChangedButton, TrayPosition(element, e.GetPosition(element)), doubleClickTime);
        e.Handled = true;
        element.CaptureMouse();
    }

    private void TrayIcon_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ManagedShell.WindowsTray.NotifyIcon icon } element)
            return;
        element.ReleaseMouseCapture();
        if (element.IsMouseOver)
            icon.IconMouseUp(e.ChangedButton, TrayPosition(element, e.GetPosition(element)), (int)Tempo.App.Interop.NativeMethods.GetDoubleClickTime());
        e.Handled = true;
    }

    private void TrayIcon_MouseEnter(object sender, MouseEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (element.DataContext is ManagedShell.WindowsTray.NotifyIcon icon)
            icon.IconMouseEnter(TrayPosition(element, e.GetPosition(element)));
    }

    private void TrayIcon_MouseLeave(object sender, MouseEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (element.DataContext is ManagedShell.WindowsTray.NotifyIcon icon)
            icon.IconMouseLeave(TrayPosition(element, e.GetPosition(element)));
    }

    private void TrayIcon_MouseMove(object sender, MouseEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (element.DataContext is ManagedShell.WindowsTray.NotifyIcon icon)
            icon.IconMouseMove(TrayPosition(element, e.GetPosition(element)));
    }

    private void TrayIcon_Click(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (element.DataContext is not ManagedShell.WindowsTray.NotifyIcon icon)
            return;
        var position = TrayPosition(element, new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        icon.IconMouseDown(MouseButton.Left, position, -1);
        icon.IconMouseUp(MouseButton.Left, position, (int)Tempo.App.Interop.NativeMethods.GetDoubleClickTime());
    }

    // ───────────── Apps: drag an icon to reorder ─────────────
    // A press starts nothing; moving past the drag threshold takes mouse capture away from the
    // button (so its click never fires), then the dragged entry follows the pointer live.

    private AppEntry? _pressed;
    private Point _pressPoint;
    private bool _dragging;
    private double _pressOffset;
    private FrameworkElement? _draggedContainer;

    private void Apps_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = (e.OriginalSource as FrameworkElement)?.DataContext as AppEntry;
        _pressPoint = e.GetPosition((IInputElement)sender);
        _dragging = false;
        var host = (ItemsControl)sender;
        if (_pressed is not null && host.ItemContainerGenerator.ContainerFromItem(_pressed) is FrameworkElement container)
            _pressOffset = e.GetPosition(container).X;
    }

    private void Apps_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed)
            return;
        if (_pressed.Owner.Context.Services.Settings.Current.Bar.IsLocked)
        {
            if (_dragging)
                ((ItemsControl)sender).ReleaseMouseCapture();
            FinishDrag();
            return;
        }
        var host = (ItemsControl)sender;
        var position = e.GetPosition(host);

        if (!_dragging)
        {
            if (Math.Abs(position.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance)
                return;
            _dragging = true;
            _pressed.Owner.BeginReorder(_pressed);
            host.CaptureMouse();
        }

        // Target slot = how many other buttons' centres lie left of the pointer.
        var target = 0;
        for (var i = 0; i < host.Items.Count; i++)
        {
            if (host.Items[i] == _pressed || host.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                continue;
            var centre = container.TranslatePoint(new Point(container.ActualWidth / 2, 0), host).X
                - ((container.RenderTransform as TranslateTransform)?.X ?? 0);
            if (position.X > centre)
                target++;
        }
        _pressed.Owner.MoveDragged(target);
        host.UpdateLayout();
        if (host.ItemContainerGenerator.ContainerFromItem(_pressed) is FrameworkElement dragged)
        {
            _draggedContainer = dragged;
            Panel.SetZIndex(dragged, 1);
            var transform = dragged.RenderTransform as TranslateTransform;
            if (transform is null)
                dragged.RenderTransform = transform = new TranslateTransform();
            var slot = dragged.TranslatePoint(new Point(), host).X - transform.X;
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            var group = host.Items.Cast<AppEntry>().Where(entry => entry.IsPinned == _pressed.IsPinned).ToList();
            var first = (FrameworkElement)host.ItemContainerGenerator.ContainerFromItem(group[0]);
            var last = (FrameworkElement)host.ItemContainerGenerator.ContainerFromItem(group[^1]);
            var left = first.TranslatePoint(new Point(), host).X
                - ((first.RenderTransform as TranslateTransform)?.X ?? 0);
            var right = last.TranslatePoint(new Point(), host).X
                - ((last.RenderTransform as TranslateTransform)?.X ?? 0) + last.ActualWidth;
            transform.X = Math.Clamp(position.X - _pressOffset, left, Math.Max(left, right - dragged.ActualWidth)) - slot;
        }
        e.Handled = true;
    }

    private void Apps_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            e.Handled = true; // not a click
            ((ItemsControl)sender).ReleaseMouseCapture();
        }
        FinishDrag();
    }

    private void Apps_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dragging && Mouse.Captured != sender)
            FinishDrag();
    }

    private void FinishDrag()
    {
        if (_draggedContainer is { } container)
        {
            if (container.RenderTransform is TranslateTransform transform)
                AppsPanel.Settle(transform);
            Panel.SetZIndex(container, 0);
            _draggedContainer = null;
        }
        if (_dragging)
            _pressed?.Owner.EndReorder();
        _pressed = null;
        _dragging = false;
    }

    /// <summary>The app context menu depends on pin and window state, so it's built when opened.</summary>
    private void AppButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppEntry entry, ContextMenu: { } menu })
            return;

        var apps = entry.Owner;
        menu.Items.Clear();
        menu.Items.Add(Item(entry.IsRunning ? "New window" : "Open", "\uE8A7", apps.LaunchCommand, entry, entry.LaunchTarget is not null));
        var appId = entry.Pin?.Aumid ?? (entry.Key.StartsWith("aumid:", StringComparison.Ordinal) ? entry.Key[6..] : null);
        appId ??= Shell.AppJumpList.ResolveAppId(entry.LaunchTarget);
        foreach (var group in Shell.AppJumpList.Read(appId))
        {
            var parent = group.IsTasks ? null : new MenuItem { Header = group.Name };
            foreach (var action in group.Actions)
            {
                var item = new MenuItem { Header = new TextBlock { Text = action.Name } };
                item.Click += (_, _) => Shell.ShellActions.Launch(action.Target);
                if (parent is null) menu.Items.Add(item);
                else parent.Items.Add(item);
            }
            if (parent is not null) menu.Items.Add(parent);
        }
        if (entry.IsPinned)
            menu.Items.Add(Item("Unpin from Tempo", "\uE77A", apps.UnpinCommand, entry));
        else
            menu.Items.Add(Item("Pin to Tempo", "\uE718", apps.PinToTempoCommand, entry, entry.PinTarget is not null));
        if (entry.IsRunning)
        {
            menu.Items.Add(Item(entry.Windows.Count > 1 ? "Close all windows" : "Close window", "\uE711", apps.CloseAllCommand, entry));
        }
    }

    private static MenuItem Item(string header, string glyph, ICommand command, object parameter, bool enabled = true) => new()
    {
        Header = header,
        Tag = glyph,
        Command = command,
        CommandParameter = parameter,
        IsEnabled = enabled,
    };
}


