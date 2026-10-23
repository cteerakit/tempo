using CommunityToolkit.Mvvm.Input;
using Tempo.Core.Settings;

namespace Tempo.App.Widgets;

internal sealed partial class PinSetSwitcherWidget : WidgetViewModelBase
{
    public PinSetSwitcherWidget(WidgetContext context) : base(context)
    {
        Services.Settings.Changed += Refresh;
        Own(() => Services.Settings.Changed -= Refresh);
        Refresh();
    }

    private PinSetDefinition ActiveSet => Services.Settings.Current.PinSets.Sets.First(s => s.Id == Services.Settings.Current.PinSets.ActiveId);
    public string ActiveName => ActiveSet.Name;
    public string ActiveIcon => ActiveSet.Icon;
    public bool ShowText => !ActiveSet.IconOnly;

    [RelayCommand]
    private void CycleSet()
    {
        var catalog = Services.Settings.Current.PinSets;
        if (catalog.Sets.Count < 2)
            return;
        var index = catalog.Sets.FindIndex(s => s.Id == catalog.ActiveId);
        var next = catalog.Sets[(index + 1) % catalog.Sets.Count];
        Services.Settings.Update(s => s.PinSets.ActiveId = next.Id);
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(ActiveName));
        OnPropertyChanged(nameof(ActiveIcon));
        OnPropertyChanged(nameof(ShowText));
    }
}
