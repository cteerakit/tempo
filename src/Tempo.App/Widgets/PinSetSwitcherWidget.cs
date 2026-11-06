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
    public IReadOnlyList<PinSetOption> Sets
    {
        get
        {
            var catalog = Services.Settings.Current.PinSets;
            return catalog.Sets.Select(s => new PinSetOption(s.Id, s.Name, s.Icon, s.Id == catalog.ActiveId)).ToList();
        }
    }

    /// <summary>Two sets toggle directly; three or more open the selector.</summary>
    [RelayCommand]
    private void Click()
    {
        var catalog = Services.Settings.Current.PinSets;
        if (catalog.Sets.Count > 2)
        {
            IsFlyoutOpen = !IsFlyoutOpen;
            return;
        }
        if (catalog.Sets.Count < 2)
            return;
        var index = catalog.Sets.FindIndex(s => s.Id == catalog.ActiveId);
        var next = catalog.Sets[(index + 1) % catalog.Sets.Count];
        Services.Settings.Update(s => s.PinSets.ActiveId = next.Id);
    }

    [RelayCommand]
    private void SelectSet(PinSetOption option)
    {
        IsFlyoutOpen = false;
        Services.Settings.Update(s => s.PinSets.ActiveId = option.Id);
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(ActiveName));
        OnPropertyChanged(nameof(ActiveIcon));
        OnPropertyChanged(nameof(ShowText));
        OnPropertyChanged(nameof(Sets));
    }
}

internal sealed record PinSetOption(string Id, string Name, string Icon, bool IsActive);
