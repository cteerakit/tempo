using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tempo.Core.Settings;

namespace Tempo.App.Settings;

internal sealed partial class SettingsViewModel
{
    public IReadOnlyList<PinSetDefinition> Sets => S.PinSets.Sets.ToArray();
    public IReadOnlyList<Option<string>> SetIcons { get; } =
    [
        new("\uE8F1", "Apps"), new("\uE821", "Work"), new("\uE80F", "Home"),
        new("\uE7FC", "Games"), new("\uE8D6", "Study"), new("\uE8D2", "Code"),
        new("\uE8D4", "Music"), new("\uE8B9", "Photos"), new("\uE734", "Star"),
        new("\uE774", "Travel"),
    ];

    public string SetIcon
    {
        get => SelectedSet?.Icon ?? "\uE8F1";
        set
        {
            if (value is null || SelectedSet is not { } selected || selected.Icon == value) return;
            Update(_ => selected.Icon = value);
        }
    }

    public bool SetIconOnly
    {
        get => SelectedSet?.IconOnly ?? false;
        set
        {
            if (SelectedSet is not { } selected || selected.IconOnly == value) return;
            Update(_ => selected.IconOnly = value);
        }
    }
    public PinSetDefinition? SelectedSet
    {
        get => Sets.FirstOrDefault(s => s.Id == S.PinSets.ActiveId);
        set
        {
            if (value is null || value.Id == S.PinSets.ActiveId) return;
            Update(s => s.PinSets.ActiveId = value.Id);
        }
    }

    [ObservableProperty]
    public partial string SetName { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    private bool ValidateName(bool renaming)
    {
        var name = SetName.Trim();
        if (name.Length is 0 or > 60)
        {
            Status = "Enter a name of 1–60 characters.";
            return false;
        }
        if (Sets.Any(s => (!renaming || s.Id != SelectedSet?.Id) && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Status = "A set with that name already exists.";
            return false;
        }
        Status = "";
        return true;
    }

    [RelayCommand]
    private void Create()
    {
        if (!ValidateName(false)) return;
        var set = new PinSetDefinition { Id = Guid.NewGuid().ToString("N"), Name = SetName.Trim() };
        Update(s =>
        {
            s.PinSets.Sets.Add(set);
            s.PinSets.ActiveId = set.Id;
        });
        SetName = "";
        Status = "Set created. Pin running apps from their context menu.";
    }

    [RelayCommand]
    private void Rename()
    {
        if (!ValidateName(true) || SelectedSet is not { } selected) return;
        Update(_ => selected.Name = SetName.Trim());
        SetName = "";
        Status = "Set renamed.";
    }

    /// <summary>Asks before a destructive change; replaceable so automated checks don't block on a dialog.</summary>
    internal Func<string, bool> Confirm { get; set; } = message =>
        System.Windows.MessageBox.Show(
            System.Windows.Application.Current?.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)!,
            message, "Delete pinned app set", System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;

    // The default set is the fallback for every other one, so it always exists.
    private bool CanDelete() => SelectedSet is { Id: not PinSetCatalog.DefaultId };

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedSet is not { } selected || !CanDelete()) return;
        if (!Confirm($"Delete the set \"{selected.Name}\"? Its pinned apps and order will be removed.")) return;
        Update(s =>
        {
            var index = s.PinSets.Sets.FindIndex(x => x.Id == selected.Id);
            if (index < 0) return;
            s.PinSets.Sets.RemoveAt(index);
            s.PinSets.ActiveId = s.PinSets.Sets[Math.Max(0, index - 1)].Id;
        });
        SetName = "";
        Status = $"Set \"{selected.Name}\" deleted.";
    }

}
