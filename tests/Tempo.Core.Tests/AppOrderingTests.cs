using Tempo.Core.Shell;

namespace Tempo.Core.Tests;

public class AppOrderingTests
{
    [Fact]
    public void Apply_SortsPlacedItemsAndAppendsTheRestInNaturalOrder()
    {
        string[] natural = ["pin:a", "pin:b", "exe:new1", "pin:c", "exe:new2"];

        var ordered = AppOrdering.Apply(natural, k => k, ["PIN:C", "pin:a", "pin:gone"]);

        Assert.Equal(["pin:c", "pin:a", "pin:b", "exe:new1", "exe:new2"], ordered);
    }

    [Fact]
    public void Apply_WithNoSavedOrder_KeepsNaturalOrder()
    {
        Assert.Equal(["b", "a"], AppOrdering.Apply(["b", "a"], k => k, []));
    }

    [Fact]
    public void Apply_KeepsPinnedAppsFirstDespiteMixedSavedOrder()
    {
        string[] natural = ["pin:a", "pin:b", "exe:a", "exe:b", "exe:new"];

        var ordered = AppOrdering.Apply(natural, k => k,
            ["exe:b", "pin:b", "exe:a", "pin:a"], k => k.StartsWith("pin:"));

        Assert.Equal(["pin:b", "pin:a", "exe:b", "exe:a", "exe:new"], ordered);
    }

    [Fact]
    public void Apply_GroupsAppsWithoutSavedOrder()
    {
        Assert.Equal(["pin:a", "exe:a"], AppOrdering.Apply(
            ["exe:a", "pin:a"], k => k, [], k => k.StartsWith("pin:")));
    }

    [Fact]
    public void Merge_KeepsClosedAppsAndDropsWindowKeysAndDuplicates()
    {
        var merged = AppOrdering.Merge(["pin:b", "hwnd:123", "pin:a"], ["pin:a", "exe:closed", "hwnd:9", "PIN:B"]);

        Assert.Equal(["pin:b", "pin:a", "exe:closed"], merged);
    }
}
