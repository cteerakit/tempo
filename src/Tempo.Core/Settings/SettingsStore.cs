namespace Tempo.Core.Settings;

public sealed record SettingsLoadResult(TempoSettings Settings, string? Warning);

/// <summary>Reads and writes the settings file. Writes are atomic so a crash never leaves a half-written file.</summary>
public sealed class SettingsStore(string path)
{
    public string Path { get; } = path;

    public SettingsLoadResult Load()
    {
        if (!File.Exists(Path))
            return new(TempoSettings.CreateDefault(), null);

        string json;
        try
        {
            json = File.ReadAllText(Path);
        }
        catch (IOException ex)
        {
            return new(TempoSettings.CreateDefault(), $"Could not read settings: {ex.Message}");
        }

        try
        {
            return new(SettingsSerializer.Deserialize(json), null);
        }
        catch (SettingsFormatException ex)
        {
            var backup = BackUp();
            return new(TempoSettings.CreateDefault(), $"{ex.Message} Defaults were loaded; the old file was saved as {backup}.");
        }
    }

    /// <summary>Parses settings text without touching the file system (used for hot reload).</summary>
    public static TempoSettings Parse(string json) => SettingsSerializer.Deserialize(json);

    /// <returns>The exact text written, so callers can recognise their own write in a file watcher.</returns>
    public string Save(TempoSettings settings)
    {
        var json = SettingsSerializer.Serialize(settings);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

        var temp = Path + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(Path))
            File.Replace(temp, Path, destinationBackupFileName: null);
        else
            File.Move(temp, Path);
        return json;
    }

    private string BackUp()
    {
        var backup = $"{Path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        try
        {
            File.Copy(Path, backup, overwrite: true);
        }
        catch (IOException)
        {
        }
        return backup;
    }
}
