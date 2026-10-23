using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Tempo.Core.Layout;

namespace Tempo.Core.Settings;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(TempoSettings))]
internal sealed partial class TempoJsonContext : JsonSerializerContext;

public sealed class SettingsFormatException(string message, Exception? inner = null) : Exception(message, inner);

public static class SettingsSerializer
{
    /// <summary>Upgrades a document from version N to N+1. Index = source version.</summary>
    private static readonly Action<JsonObject>[] Migrations =
    [
        // v0 → v1: files written before versioning existed. The shape is unchanged; just stamp the version.
        _ => { },
        // v1 → v2: the free-form bar height became three sizes; pick the nearest one.
        root =>
        {
            if (root["bar"] is not JsonObject bar)
                return;
            // Names as they were defined in v2 (32 / 40 / 48 px); the v2 → v3 step renames them.
            if (bar["height"] is JsonValue h && h.TryGetValue(out double height))
                bar["size"] = height < 36 ? "Small" : height < 44 ? "Medium" : "Large";
            bar.Remove("height");
        },
        // v2 → v3: sizes shifted up a step (old Medium 40 px is the new Small, old Large 48 px the new
        // Medium) and a bigger Large was added. Map names so the bar keeps its current size.
        root =>
        {
            if (root["bar"] is JsonObject bar && bar["size"] is JsonValue v && v.TryGetValue(out string? size))
                bar["size"] = size switch { "Large" => "Medium", _ => "Small" };
        },
    ];

    public static string Serialize(TempoSettings settings) =>
        JsonSerializer.Serialize(settings, TempoJsonContext.Default.TempoSettings);

    public static TempoSettings Deserialize(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                   ?? throw new SettingsFormatException("Settings root must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new SettingsFormatException($"Settings file is not valid JSON: {ex.Message}", ex);
        }

        Migrate(root);

        TempoSettings? settings;
        try
        {
            settings = root.Deserialize(TempoJsonContext.Default.TempoSettings);
        }
        catch (JsonException ex)
        {
            throw new SettingsFormatException($"Settings file has an invalid value: {ex.Message}", ex);
        }

        return Normalize(settings ?? TempoSettings.CreateDefault());
    }

    internal static void Migrate(JsonObject root)
    {
        var version = 0;
        if (root["schemaVersion"] is JsonValue v && v.TryGetValue(out int parsed))
            version = parsed;

        if (version > TempoSettings.CurrentSchemaVersion)
            throw new SettingsFormatException(
                $"Settings were written by a newer Tempo (schema {version}, this build understands {TempoSettings.CurrentSchemaVersion}).");

        for (; version < TempoSettings.CurrentSchemaVersion; version++)
            Migrations[version](root);

        root["schemaVersion"] = TempoSettings.CurrentSchemaVersion;
    }

    /// <summary>Repairs values a hand-edited file could break: nulls, out-of-range numbers, duplicate ids.</summary>
    public static TempoSettings Normalize(TempoSettings s)
    {
        s.SchemaVersion = TempoSettings.CurrentSchemaVersion;
        s.Bar ??= new();
        s.Behavior ??= new();
        s.Appearance ??= new();
        s.Layout ??= LayoutSettings.CreateDefault();
        s.PinSets ??= new();
        s.PinSets.Normalize();

        if (!Enum.IsDefined(s.Bar.Size))
            s.Bar.Size = BarSize.Medium;
        s.Bar.Margin = Clamp(s.Bar.Margin, BarSettings.MinMargin, BarSettings.MaxMargin, 10);
        if (string.IsNullOrWhiteSpace(s.Bar.Monitor))
            s.Bar.Monitor = null;

        var a = s.Appearance;
        if (string.IsNullOrWhiteSpace(a.ThemeId))
            a.ThemeId = "Auto";
        a.BlurStrength = Clamp(a.BlurStrength, 0, 100, 100);
        if (a.TintOpacity is { } t)
            a.TintOpacity = Clamp(t, 0, 1, 0.5);
        if (a.CornerRadius is { } r)
            a.CornerRadius = Clamp(r, AppearanceSettings.MinCornerRadius, AppearanceSettings.MaxCornerRadius, 12);
        if (a.FontSize is { } f)
            a.FontSize = Clamp(f, AppearanceSettings.MinFontSize, AppearanceSettings.MaxFontSize, 13);
        if (string.IsNullOrWhiteSpace(a.FontFamily))
            a.FontFamily = null;
        if (string.IsNullOrWhiteSpace(a.AccentColor))
            a.AccentColor = null;

        var seen = new HashSet<string>();
        foreach (var zone in Enum.GetValues<WidgetZone>())
        {
            var list = s.Layout.GetZone(zone) ?? [];
            list.RemoveAll(w => w is null || string.IsNullOrWhiteSpace(w.TypeId));
            foreach (var w in list)
            {
                w.Settings ??= [];
                if (string.IsNullOrWhiteSpace(w.InstanceId) || !seen.Add(w.InstanceId))
                {
                    w.InstanceId = WidgetInstance.NewId();
                    seen.Add(w.InstanceId);
                }
            }
            switch (zone)
            {
                case WidgetZone.Left: s.Layout.Left = list; break;
                case WidgetZone.Center: s.Layout.Center = list; break;
                case WidgetZone.Right: s.Layout.Right = list; break;
            }
        }

        return s;
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
