using Tempo.Core.Settings;

namespace Tempo.Core.Tests;

public class PinSetTests
{
    [Fact]
    public void LegacySettings_KeepDefaultSetSelected()
    {
        var settings = SettingsSerializer.Deserialize("""{"schemaVersion":3}""");
        Assert.Equal("default", settings.PinSets.ActiveId);
        Assert.Equal("Default", Assert.Single(settings.PinSets.Sets).Name);
    }

    [Fact]
    public void InvalidCatalog_RepairsIdsNamesAndSelection()
    {
        var settings = SettingsSerializer.Deserialize("""
            {"schemaVersion":3,"pinSets":{"activeId":"missing","sets":[
                null,{"id":"work","name":"  Work  "},{"id":"work","name":"Duplicate"},{"id":"","name":"Invalid"}
            ]}}
            """);
        Assert.Equal("default", settings.PinSets.ActiveId);
        Assert.Equal(["Default", "Work"], settings.PinSets.Sets.Select(s => s.Name));
        var restored = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(settings));
        Assert.Equal(["default", "work"], restored.PinSets.Sets.Select(s => s.Id));
    }
}
