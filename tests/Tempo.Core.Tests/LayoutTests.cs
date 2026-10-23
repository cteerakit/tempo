using Tempo.Core.Layout;

namespace Tempo.Core.Tests;

public class LayoutTests
{
    private static (LayoutSettings Layout, string A, string B, string C) ThreeInLeft()
    {
        var layout = new LayoutSettings();
        var a = layout.Add("a", WidgetZone.Left).InstanceId;
        var b = layout.Add("b", WidgetZone.Left).InstanceId;
        var c = layout.Add("c", WidgetZone.Left).InstanceId;
        return (layout, a, b, c);
    }

    private static string[] Types(List<WidgetInstance> list) => list.Select(w => w.TypeId).ToArray();

    [Fact]
    public void Move_Forward_WithinZone_UsesPreMoveInsertionIndex()
    {
        var (layout, a, _, _) = ThreeInLeft();

        // Dropping "a" between "b" and "c" means insertion index 2 in the original list.
        Assert.True(layout.Move(a, WidgetZone.Left, 2));

        Assert.Equal(["b", "a", "c"], Types(layout.Left));
    }

    [Fact]
    public void Move_Backward_WithinZone()
    {
        var (layout, _, _, c) = ThreeInLeft();

        layout.Move(c, WidgetZone.Left, 0);

        Assert.Equal(["c", "a", "b"], Types(layout.Left));
    }

    [Fact]
    public void Move_ToSameSlot_ReportsNoChange()
    {
        var (layout, _, b, _) = ThreeInLeft();

        Assert.False(layout.Move(b, WidgetZone.Left, 1));
        Assert.False(layout.Move(b, WidgetZone.Left, 2));
        Assert.Equal(["a", "b", "c"], Types(layout.Left));
    }

    [Fact]
    public void Move_AcrossZones_ClampsIndex()
    {
        var (layout, _, b, _) = ThreeInLeft();
        layout.Add("x", WidgetZone.Right);

        layout.Move(b, WidgetZone.Right, 99);

        Assert.Equal(["a", "c"], Types(layout.Left));
        Assert.Equal(["x", "b"], Types(layout.Right));
        Assert.True(layout.TryFind(b, out var zone, out var index));
        Assert.Equal((WidgetZone.Right, 1), (zone, index));
    }

    [Fact]
    public void AddAndRemove()
    {
        var layout = new LayoutSettings();
        var first = layout.Add("first", WidgetZone.Center);
        layout.Add("zeroth", WidgetZone.Center, index: -5);

        Assert.Equal(["zeroth", "first"], Types(layout.Center));
        Assert.True(layout.Remove(first.InstanceId));
        Assert.False(layout.Remove(first.InstanceId));
        Assert.False(layout.Move("missing", WidgetZone.Left, 0));
    }

    [Fact]
    public void DefaultLayout_HasUniqueIds()
    {
        var ids = LayoutSettings.CreateDefault().EnumerateAll().Select(x => x.Instance.InstanceId).ToList();

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
