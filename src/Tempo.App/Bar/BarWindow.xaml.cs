using System.Windows;
using System.Windows.Controls;
using Tempo.Core.Layout;
using Tempo.App.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Tempo.App.Interop;
using Tempo.App.Shell;
using Tempo.App.Widgets;
using Tempo.Core.Settings;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Bar;

/// <summary>
/// The bar itself: a borderless, never-activated, topmost tool window registered as a shell app bar.
/// Placement is done in physical pixels against the chosen monitor.
/// </summary>
internal partial class BarWindow : Window
{
    private readonly AppServices _services;
    private readonly string? _monitor;
    private readonly BarViewModel _viewModel;
    private readonly uint _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private nint _hwnd;
    private BarGlass? _glass;
    private AppBarService? _appBar;
    private bool _placementQueued;
    private bool _hiddenForFullscreen;
    private bool _placementDeferred;

    public BarWindow(AppServices services) : this(services, null) { }

    public BarWindow(AppServices services, string? monitor)
    {
        _services = services;
        _monitor = monitor;
        _viewModel = new BarViewModel(services);
        DataContext = _viewModel;
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => QueuePlacement();
        DpiChanged += (_, _) => QueuePlacement();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) _glass?.Hide();
            else QueuePlacement();
        };
        Closed += OnClosed;
    }

    private TempoSettings Settings => _services.Settings.Current;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        source.AddHook(WndProc);
        WindowEffects.MakeToolbarWindow(_hwnd);

        _appBar = new AppBarService(_hwnd);
        _appBar.PositionChanged += QueuePlacement;
        _appBar.FullscreenChanged += OnFullscreenChanged;

        _services.Settings.Changed += ApplySettings;
        _services.Theme.Changed += ApplyTheme;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        InputManager.Current.PostProcessInput += OnPostProcessInput;

        ApplySettings();
    }

    private void ApplySettings()
    {
        if (_hwnd == 0)
            return;
        _services.Bar.Scale = Monitors.Find(_monitor ?? Settings.Bar.Monitor).Scale;
        _services.Bar.Edge = Settings.Bar.Edge;
        _services.Bar.IsLocked = Settings.Bar.IsLocked;
        var metrics = Settings.Bar.Metrics;
        _services.Bar.IconSize = metrics.IconSize;
        _services.Bar.GlyphSize = metrics.GlyphSize;
        Application.Current.Resources["Bar.GlyphSize"] = metrics.GlyphSize;
        Application.Current.Resources["Bar.SmallGlyphSize"] = metrics.GlyphSize - 2;
        _viewModel.Reconcile(Settings.Layout);
        _services.Theme.Apply(Settings.Appearance); // raises Theme.Changed → ApplyTheme → placement
    }

    private const string WidgetDragFormat = "Tempo.BarWidget";
    private Point _widgetPressPoint;
    private FrameworkElement? _pressedWidget;
    private WidgetViewModelBase? _draggedWidget;
    private double _widgetPressOffset;
    private FrameworkElement? _draggedWidgetContainer;

    private void Widget_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressedWidget = null;
        if (Settings.Bar.IsLocked)
            return;

        // Individual app icons keep their own reorder gesture.
        for (var element = e.OriginalSource as DependencyObject; element is not null && element != sender;
             element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
        {
            if (element is FrameworkElement { DataContext: AppEntry } or Thumb or Slider)
                return;
        }

        _widgetPressPoint = e.GetPosition(this);
        _pressedWidget = (FrameworkElement)sender;
        _widgetPressOffset = e.GetPosition(_pressedWidget).X;
    }

    private void Widget_MouseUp(object sender, MouseButtonEventArgs e) => _pressedWidget = null;

    private void Widget_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedWidget != sender || sender is not FrameworkElement { DataContext: WidgetViewModelBase widget } surface)
            return;
        if (Settings.Bar.IsLocked || e.LeftButton != MouseButtonState.Pressed)
        {
            _pressedWidget = null;
            return;
        }
        var delta = e.GetPosition(this) - _widgetPressPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _pressedWidget = null;
        // Take capture away from the pressed button so releasing a drag cannot click it.
        surface.CaptureMouse();
        _draggedWidget = widget;
        widget.IsDragging = true;
        e.Handled = true;
        try
        {
            DragDrop.DoDragDrop(surface, new DataObject(WidgetDragFormat, widget.InstanceId), DragDropEffects.Move);
        }
        finally
        {
            surface.ReleaseMouseCapture();
            FinishWidgetDrag();
        }
    }

    private void FinishWidgetDrag()
    {
        if (_draggedWidget is { } widget)
            widget.IsDragging = false;
        _draggedWidget = null;
        if (_draggedWidgetContainer is { } container)
        {
            container.IsHitTestVisible = true;
            Panel.SetZIndex(container, 0);
            if (container.RenderTransform is TranslateTransform transform)
                AppsPanel.Settle(transform);
        }
        _draggedWidgetContainer = null;
        foreach (var host in new[] { LeftZone, CenterZone, RightZone })
            Panel.SetZIndex(host, 0);
        _viewModel.Reconcile(Settings.Layout);
    }

    private bool CanDropWidget(DragEventArgs e) => !Settings.Bar.IsLocked &&
        e.Data.GetData(WidgetDragFormat) is string id && Settings.Layout.Find(id) is not null;

    private (WidgetZone Zone, ItemsControl Host, int Index) WidgetDropTarget(Point position)
    {
        var centerStart = LeftColumn.ActualWidth;
        var rightStart = centerStart + Zones.ColumnDefinitions[1].ActualWidth;
        var zone = position.X < centerStart ? WidgetZone.Left : position.X < rightStart ? WidgetZone.Center : WidgetZone.Right;
        var host = zone == WidgetZone.Left ? LeftZone : zone == WidgetZone.Center ? CenterZone : RightZone;
        var index = host.Items.Count;
        for (var i = 0; i < host.Items.Count; i++)
        {
            if (host.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement { IsVisible: true } container)
            {
                var transform = (container.RenderTransform as TranslateTransform)?.X ?? 0;
                var center = container.TranslatePoint(new Point(container.ActualWidth / 2, 0), Zones).X - transform;
                if (position.X < center)
                {
                    index = i;
                    break;
                }
            }
        }
        return (zone, host, index);
    }

    private void PreviewWidgetDrag(Point position)
    {
        if (_draggedWidget is not { } widget || Settings.Bar.IsLocked)
            return;
        var target = WidgetDropTarget(position);
        _viewModel.PreviewMove(widget, target.Zone, target.Index);
        Zones.UpdateLayout();
        if (target.Host.ItemContainerGenerator.ContainerFromItem(widget) is not FrameworkElement container)
            return;
        if (_draggedWidgetContainer is { } previous && previous != container)
        {
            previous.IsHitTestVisible = true;
            Panel.SetZIndex(previous, 0);
        }
        _draggedWidgetContainer = container;
        container.IsHitTestVisible = false;
        Panel.SetZIndex(container, 1);
        foreach (var host in new[] { LeftZone, CenterZone, RightZone })
            Panel.SetZIndex(host, host == target.Host ? 1 : 0);
        if (container.RenderTransform is not TranslateTransform transform)
            container.RenderTransform = transform = new TranslateTransform();
        var slot = container.TranslatePoint(new Point(), Zones).X - transform.X;
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = Math.Clamp(position.X - _widgetPressOffset, 0, Math.Max(0, Zones.ActualWidth - container.ActualWidth)) - slot;
    }

    private void Zones_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanDropWidget(e) ? DragDropEffects.Move : DragDropEffects.None;
        if (e.Effects == DragDropEffects.Move)
            PreviewWidgetDrag(e.GetPosition(Zones));
        e.Handled = true;
    }

    private void Zones_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (!CanDropWidget(e))
            return;

        var position = e.GetPosition(Zones);
        var target = WidgetDropTarget(position);
        var index = target.Index;
        var zone = target.Zone;
        var id = (string)e.Data.GetData(WidgetDragFormat);
        if (_draggedWidget is { } widget)
        {
            PreviewWidgetDrag(position);
            var host = new[] { LeftZone, CenterZone, RightZone }.Single(h => h.Items.Contains(widget));
            zone = host == LeftZone ? WidgetZone.Left : host == CenterZone ? WidgetZone.Center : WidgetZone.Right;
            index = host.Items.IndexOf(widget);
            if (Settings.Layout.TryFind(id, out var fromZone, out var fromIndex) && fromZone == zone && fromIndex < index)
                index++;
        }
        _services.Settings.Update(s =>
        {
            if (!s.Bar.IsLocked)
                s.Layout.Move(id, zone, index);
        });
        e.Effects = DragDropEffects.Move;
    }

    private void Root_ContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
    {
        LockBarMenuItem.Header = Settings.Bar.IsLocked ? "Unlock bar" : "Lock bar";
        LockBarMenuItem.Tag = Settings.Bar.IsLocked ? "\uE785" : "\uE72E";
        TaskbarMenuItem.Header = _services.Taskbar.IsSuspended ? "Hide Windows taskbar" : "Show Windows taskbar";
        TaskbarMenuItem.Visibility = Settings.Behavior.TaskbarMode == TaskbarMode.None ? Visibility.Collapsed : Visibility.Visible;

        var menu = Root.ContextMenu;
        var isAppMenu = false;
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is FrameworkElement { ContextMenu: { } foundMenu })
            {
                menu = foundMenu;
                isAppMenu = element is FrameworkElement { DataContext: AppEntry };
                break;
            }
        if (menu is null)
            return;

        // Rebuild the host section without disturbing widget-specific commands.
        for (var i = menu.Items.Count - 1; i >= 0; i--)
            if (menu.Items[i] is FrameworkElement generated && ReferenceEquals(generated.Tag, _widgetMenuMarker))
                menu.Items.RemoveAt(i);

        WidgetViewModelBase? widget = null;
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is FrameworkElement { DataContext: WidgetViewModelBase found })
            {
                widget = found;
                break;
            }

        if (!isAppMenu && widget is not null && Settings.Layout.Find(widget.InstanceId) is { } instance &&
            _services.Widgets.Find(instance.TypeId) is { } descriptor)
        {
            var id = widget.InstanceId;
            var options = new MenuItem { Header = $"{descriptor.DisplayName} options...", Tag = _widgetMenuMarker };
            options.Click += (_, _) => OpenWidgetOptions(id, descriptor);
            menu.Items.Insert(0, options);
            var position = 1;
            foreach (var zone in Enum.GetValues<WidgetZone>())
            {
                var target = zone;
                var move = new MenuItem { Header = $"Move to {zone.ToString().ToLowerInvariant()}", IsEnabled = !Settings.Bar.IsLocked, Tag = _widgetMenuMarker };
                move.Click += (_, _) => _services.Settings.Update(s =>
                {
                    if (!s.Bar.IsLocked) s.Layout.Move(id, target, int.MaxValue);
                });
                menu.Items.Insert(position++, move);
            }
            var remove = new MenuItem { Header = "Remove widget", Tag = _widgetMenuMarker };
            remove.Click += (_, _) => _services.Settings.Update(s => s.Layout.Remove(id));
            menu.Items.Insert(position++, remove);
            menu.Items.Insert(position, new Separator { Tag = _widgetMenuMarker });
        }

        if (menu == Root.ContextMenu)
            return;

        // Widget menus are populated before this event bubbles up to the bar.
        // Remove our previous section if the widget keeps its menu between openings.
        for (var index = 0; index < menu.Items.Count; index++)
        {
            if (menu.Items[index] is FrameworkElement item && ReferenceEquals(item.Tag, Root))
            {
                while (menu.Items.Count > index)
                    menu.Items.RemoveAt(index);
                break;
            }
        }
        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator { Tag = Root });
        AddBarItem("Customize Tempo\u2026", "\uE713", Customize_Click);
        AddBarItem((string)LockBarMenuItem.Header, (string)LockBarMenuItem.Tag, ToggleBarLock_Click);
        if (TaskbarMenuItem.Visibility == Visibility.Visible)
            AddBarItem((string)TaskbarMenuItem.Header, "\uE7F4", ToggleTaskbar_Click);
        menu.Items.Add(new Separator { Tag = Root });
        AddBarItem("Exit Tempo", "\uE7E8", Exit_Click);

        void AddBarItem(string header, string glyph, RoutedEventHandler click)
        {
            var item = new MenuItem { Header = header, Tag = glyph };
            item.SetResourceReference(StyleProperty, "BarMenuItem");
            item.Click += click;
            menu.Items.Add(item);
        }
    }

    private readonly object _widgetMenuMarker = new();

    private void OpenWidgetOptions(string instanceId, WidgetDescriptor descriptor)
    {
        if (Settings.Layout.Find(instanceId) is not { } instance)
            return;
        var settings = descriptor.ReadSettings(instance.Settings);
        var editor = Tempo.App.Settings.WidgetSettingsEditor.Build(settings, value =>
            _services.Settings.Update(s =>
            {
                if (s.Layout.Find(instanceId) is { } current)
                    current.Settings = descriptor.WriteSettings(value);
            }));
        var window = new Window
        {
            Title = $"{descriptor.DisplayName} options",
            Width = 400,
            Height = 480,
            MinWidth = 320,
            MinHeight = 240,
            ThemeMode = ThemeMode.System,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer
            {
                Content = editor,
                Margin = new Thickness(20),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        };
        window.Show();
    }

    private void ApplyTheme()
    {
        if (_hwnd == 0)
            return;
        var theme = _services.Theme.Current;
        WindowEffects.SetBackdrop(_hwnd, BackdropKind.None);
        _glass?.SetStrength(Settings.Appearance.BlurStrength);
        WindowEffects.SetDarkMode(_hwnd, theme.IsDark);
        UpdatePlacement();
    }

    private void QueuePlacement()
    {
        if (_placementQueued || _hwnd == 0)
            return;
        _placementQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _placementQueued = false;
            UpdatePlacement();
        });
    }

    private void UpdatePlacement()
    {
        // Updating the shell work area broadcasts settings changes, which can
        // rebuild WPF Fluent slider templates and end the current drag.
        if (Mouse.Captured is Thumb)
        {
            _placementDeferred = true;
            return;
        }
        _placementDeferred = false;
        if (_hwnd == 0 || _appBar is null)
            return;

        var bar = Settings.Bar;
        var monitor = Monitors.Find(_monitor ?? bar.Monitor);
        _services.Bar.IsOnPrimaryMonitor = monitor.IsPrimary;
        _services.Bar.Scale = monitor.Scale;
        var scale = monitor.Scale;
        var height = (int)Math.Round(bar.Height * scale);
        var pill = bar.Mode == BarMode.Pill;
        var radius = pill ? Math.Min(_services.Theme.Current.CornerRadius, bar.Height / 2) : 0;

        // Shape of the bar's surface.
        Root.CornerRadius = new CornerRadius(radius);
        Root.BorderThickness = pill ? new Thickness(1)
            : bar.Edge == BarEdge.Bottom ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 0, 1);
        LeftColumn.Width = RightColumn.Width = pill ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        CenterZone.Margin = pill ? new Thickness(6, 0, 6, 0) : new Thickness(0);

        if (!pill)
        {
            SizeToContent = SizeToContent.Manual;
            var granted = _appBar.Reserve(bar.Edge, monitor.Bounds, height);
            SetWindowPos(_hwnd, HWND_TOPMOST, granted.Left, granted.Top, granted.Width, height, SWP_NOACTIVATE);
            UpdateGlass(granted.Left, granted.Top, granted.Width, height, 0);
            return;
        }

        // Pill: WPF sizes the width to the content; Tempo centres it and keeps a gap from the edge.
        SizeToContent = SizeToContent.Width;
        Height = bar.Height;
        var margin = (int)Math.Round(bar.Margin * scale);

        RECT area;
        if (bar.ReserveSpaceInPill)
        {
            area = _appBar.Reserve(bar.Edge, monitor.Bounds, height + margin * 2);
        }
        else
        {
            _appBar.ReleaseReservation();
            area = monitor.WorkArea;
        }

        GetWindowRect(_hwnd, out var current);
        var width = current.Width;
        var x = area.Left + (area.Width - width) / 2;
        var y = bar.Edge == BarEdge.Top ? area.Top + margin : area.Bottom - margin - height;
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);

        UpdateGlass(x, y, width, height, radius * scale);
    }

    private void UpdateGlass(int x, int y, int width, int height, double radius)
    {
        WindowEffects.ClearShape(_hwnd);
        var enabled = _services.Theme.Current.Backdrop != BackdropKind.None;
        if (enabled)
            _glass ??= new BarGlass(_hwnd);
        _glass?.Update(x, y, width, height, radius, Settings.Appearance.BlurStrength,
            IsVisible && !_hiddenForFullscreen && enabled);
    }

    private void OnFullscreenChanged(bool fullscreen)
    {
        if (!Settings.Behavior.HideOnFullscreen && !_hiddenForFullscreen)
            return;

        _hiddenForFullscreen = fullscreen;
        _services.Scheduler.Paused = fullscreen;
        if (fullscreen)
        {
            _glass?.Hide();
            ShowWindow(_hwnd, SW_HIDE);
        }
        else
        {
            ShowWindow(_hwnd, SW_SHOWNA);
            QueuePlacement();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(QueuePlacement);

    private void OnPostProcessInput(object sender, ProcessInputEventArgs e)
    {
        if (_placementDeferred && Mouse.Captured is not Thumb)
            QueuePlacement();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            // Clicking the bar must never take focus away from the user's app.
            handled = true;
            return MA_NOACTIVATE;
        }
        if (msg == _taskbarCreatedMessage)
        {
            // Explorer restarted: it has forgotten our app bar and re-shown its taskbar.
            Log.Info("Explorer restarted; re-registering the bar.");
            _appBar?.ReRegisterAfterExplorerRestart();
            _services.Taskbar.Apply(Settings.Behavior.TaskbarMode);
            QueuePlacement();
        }
        else if (msg == WM_DISPLAYCHANGE)
        {
            QueuePlacement();
        }
        return 0;
    }

    private void Customize_Click(object sender, RoutedEventArgs e) => _services.OpenSettings();

    private void ToggleBarLock_Click(object sender, RoutedEventArgs e) =>
        _services.Settings.Update(s => s.Bar.IsLocked = !s.Bar.IsLocked);

    private void ToggleTaskbar_Click(object sender, RoutedEventArgs e)
    {
        _services.Taskbar.SetSuspended(!_services.Taskbar.IsSuspended);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void OnClosed(object? sender, EventArgs e)
    {
        _services.Settings.Changed -= ApplySettings;
        _services.Theme.Changed -= ApplyTheme;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        InputManager.Current.PostProcessInput -= OnPostProcessInput;
        _glass?.Dispose();
        _hwnd = 0;
        _viewModel.Dispose();
        _appBar?.Dispose();
    }
}
