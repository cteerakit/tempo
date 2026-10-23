using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tempo.App.Widgets;

internal sealed partial class HiddenIconsWidget(WidgetContext context) : WidgetViewModelBase(context)
{
    public ICollectionView? Icons { get; private set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Waiting for notification icons…";

    public override void Start()
    {
        try
        {
            var area = Services.TrayIcons.GetArea();
            Icons = area.AllIcons;
            OnPropertyChanged(nameof(Icons));
            void UpdateStatus() => Status = Icons.IsEmpty
                ? "No notification icons are available."
                : "Left-click to open · Right-click for options";
            System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) => UpdateStatus();
            Icons.CollectionChanged += changed;
            Own(() => Icons.CollectionChanged -= changed);
            Own(Services.TrayIcons.Release);
            UpdateStatus();
        }
        catch (InvalidOperationException ex)
        {
            Log.Error("Could not start notification icon listener", ex);
            Status = "Notification icons could not be loaded. Remove and add the widget to retry.";
        }
    }

    public override void Dispose()
    {
        IsFlyoutOpen = false;
        base.Dispose();
    }
}
