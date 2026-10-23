using System.Text.Json.Nodes;
using Tempo.Core.Layout;
using Tempo.Core.Settings;

namespace Tempo.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void RoundTrip_PreservesValuesAndWidgetSettings()
    {
        var s = TempoSettings.CreateDefault();
        s.Bar.Edge = BarEdge.Top;
        s.Bar.IsLocked = true;
        s.Bar.Monitor = BarSettings.AllMonitors;
        s.Bar.Mode = BarMode.Pill;
        s.Behavior.TaskbarMode = TaskbarMode.Hidden;
        s.Appearance.ThemeId = "Midnight";
        s.Appearance.TintOpacity = 0.4;
        s.Appearance.BlurStrength = 65;
        s.Appearance.Backdrop = BackdropKind.Blur;
        var clock = s.Layout.Add(WidgetTypes.Clock, WidgetZone.Left, 0, new JsonObject { ["showSeconds"] = true });

        var back = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(s));

        Assert.Equal(BarEdge.Top, back.Bar.Edge);
        Assert.True(back.Bar.IsLocked);
        Assert.Equal(BarSettings.AllMonitors, back.Bar.Monitor);
        Assert.Equal(BarMode.Pill, back.Bar.Mode);
        Assert.Equal(TaskbarMode.Hidden, back.Behavior.TaskbarMode);
        Assert.Equal("Midnight", back.Appearance.ThemeId);
        Assert.Equal(0.4, back.Appearance.TintOpacity);
        Assert.Equal(65, back.Appearance.BlurStrength);
        Assert.Equal(BackdropKind.Blur, back.Appearance.Backdrop);
        var restored = back.Layout.Find(clock.InstanceId);
        Assert.NotNull(restored);
        Assert.True(restored.Settings["showSeconds"]!.GetValue<bool>());
        Assert.Equal(s.Layout.EnumerateAll().Count(), back.Layout.EnumerateAll().Count());
    }

    [Fact]
    public void Serialize_WritesEnumsAsStringsAndOmitsNullOverrides()
    {
        var json = SettingsSerializer.Serialize(TempoSettings.CreateDefault());

        Assert.Contains("\"edge\": \"Bottom\"", json);
        Assert.DoesNotContain("accentColor", json);
    }

    [Fact]
    public void Deserialize_UnversionedFile_IsMigratedToCurrentVersion()
    {
        var s = SettingsSerializer.Deserialize("""{ "bar": { "edge": "Top" } }""");

        Assert.Equal(TempoSettings.CurrentSchemaVersion, s.SchemaVersion);
        Assert.Equal(BarEdge.Top, s.Bar.Edge);
        Assert.False(s.Bar.IsLocked);
        Assert.NotEmpty(s.Layout.EnumerateAll());
    }

    [Theory]
    [InlineData(24, BarSize.Small)]
    [InlineData(32, BarSize.Small)]
    [InlineData(40, BarSize.Small)]
    [InlineData(48, BarSize.Medium)]
    [InlineData(72, BarSize.Medium)]
    public void Deserialize_V1Height_MigratesToNearestSize(double height, BarSize expected)
    {
        var s = SettingsSerializer.Deserialize($$"""{ "schemaVersion": 1, "bar": { "height": {{height}} } }""");

        Assert.Equal(expected, s.Bar.Size);
        Assert.Equal(BarMetrics.For(expected).Height, s.Bar.Height);
        Assert.DoesNotContain("\"height\"", SettingsSerializer.Serialize(s));
    }

    [Theory]
    [InlineData("Small", BarSize.Small)]
    [InlineData("Medium", BarSize.Small)]
    [InlineData("Large", BarSize.Medium)]
    public void Deserialize_V2Size_KeepsTheSamePixelHeight(string v2Size, BarSize expected)
    {
        var s = SettingsSerializer.Deserialize($$"""{ "schemaVersion": 2, "bar": { "size": "{{v2Size}}" } }""");

        Assert.Equal(expected, s.Bar.Size);
    }

    [Fact]
    public void DefaultSize_IsMedium48() => Assert.Equal(48, new BarSettings().Height);

    [Fact]
    public void Deserialize_FutureVersion_Throws()
    {
        Assert.Throws<SettingsFormatException>(() => SettingsSerializer.Deserialize("""{ "schemaVersion": 999 }"""));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "bar": { "edge": "Sideways" } }""")]
    public void Deserialize_InvalidContent_Throws(string json)
    {
        Assert.Throws<SettingsFormatException>(() => SettingsSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_AcceptsCommentsAndTrailingCommas()
    {
        var s = SettingsSerializer.Deserialize("""
            {
              // hand edited
              "bar": { "height": 48, },
            }
            """);

        Assert.Equal(BarSize.Medium, s.Bar.Size);
    }

    [Fact]
    public void Normalize_RepairsRangesNullsAndDuplicateIds()
    {
        var s = SettingsSerializer.Deserialize("""
            {
              "schemaVersion": 1,
              "bar": { "height": 500, "margin": -4, "monitor": " " },
              "appearance": { "blurStrength": 200, "tintOpacity": 3, "themeId": "", "fontFamily": "" },
              "layout": {
                "left": [ { "instanceId": "a", "typeId": "tempo.clock" }, { "instanceId": "a", "typeId": "tempo.clock" } ],
                "center": null,
                "right": [ { "instanceId": "b", "typeId": "" } ]
              }
            }
            """);

        Assert.Equal(BarSize.Medium, s.Bar.Size);
        Assert.Equal(BarSettings.MinMargin, s.Bar.Margin);
        Assert.Null(s.Bar.Monitor);
        Assert.Equal(1, s.Appearance.TintOpacity);
        Assert.Equal(100, s.Appearance.BlurStrength);
        Assert.Equal("Auto", s.Appearance.ThemeId);
        Assert.Null(s.Appearance.FontFamily);
        Assert.Equal(2, s.Layout.Left.Count);
        Assert.NotEqual(s.Layout.Left[0].InstanceId, s.Layout.Left[1].InstanceId);
        Assert.Empty(s.Layout.Center);
        Assert.Empty(s.Layout.Right);
    }

    [Fact]
    public void Store_SavesAtomicallyAndBacksUpCorruptFiles()
    {
        var dir = Directory.CreateTempSubdirectory("tempo-tests");
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            var store = new SettingsStore(path);

            Assert.Null(store.Load().Warning);

            var s = TempoSettings.CreateDefault();
            s.Bar.Size = BarSize.Large;
            store.Save(s);
            store.Save(s);
            Assert.Equal(BarSize.Large, store.Load().Settings.Bar.Size);
            Assert.False(File.Exists(path + ".tmp"));

            File.WriteAllText(path, "{ broken");
            var result = store.Load();
            Assert.NotNull(result.Warning);
            Assert.Equal(BarSize.Medium, result.Settings.Bar.Size);
            Assert.Single(dir.GetFiles("*.bak"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
