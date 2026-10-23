using System.IO;
using System.Windows.Threading;
using Tempo.Core.Settings;

namespace Tempo.App.Services;

/// <summary>
/// Owns the live <see cref="TempoSettings"/>. Edits go through <see cref="Update"/>, are applied
/// immediately via <see cref="Changed"/>, and are saved shortly after. External edits to the file
/// are picked up and applied too.
/// </summary>
internal sealed class SettingsService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _reloadTimer;
    private readonly FileSystemWatcher _watcher;
    private string? _lastWrittenJson;

    public SettingsService(string path, Dispatcher dispatcher)
    {
        _store = new SettingsStore(path);
        _dispatcher = dispatcher;

        var result = _store.Load();
        Current = result.Settings;
        if (result.Warning is not null)
            Log.Error(result.Warning);
        // Write back whenever the file isn't already the complete, normalized form, so that
        // defaults filled in for missing values (like widget instance ids) stay stable.
        if (!File.Exists(path) || result.Warning is not null || ReadFileOrNull() != SettingsSerializer.Serialize(Current))
            SaveNow();

        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => SaveNow(), dispatcher) { IsEnabled = false };
        _reloadTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => ReloadFromDisk(), dispatcher) { IsEnabled = false };

        _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.EnableRaisingEvents = true;
    }

    public TempoSettings Current { get; private set; }

    private string? ReadFileOrNull()
    {
        try
        {
            return File.Exists(_store.Path) ? File.ReadAllText(_store.Path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public string FilePath => _store.Path;

    /// <summary>Raised on the UI thread whenever settings change (from the UI or the file).</summary>
    public event Action? Changed;

    public void Update(Action<TempoSettings> edit)
    {
        edit(Current);
        SettingsSerializer.Normalize(Current);
        Changed?.Invoke();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Flush()
    {
        if (_saveTimer.IsEnabled)
            SaveNow();
    }

    private void SaveNow()
    {
        _saveTimer?.Stop();
        try
        {
            _lastWrittenJson = _store.Save(Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not save settings", ex);
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) =>
        _dispatcher.BeginInvoke(() =>
        {
            _reloadTimer.Stop();
            _reloadTimer.Start();
        });

    private void ReloadFromDisk()
    {
        _reloadTimer.Stop();
        string json;
        try
        {
            json = File.ReadAllText(_store.Path);
        }
        catch (IOException)
        {
            // Still being written; the watcher will fire again.
            return;
        }

        if (json == _lastWrittenJson)
            return;

        try
        {
            Current = SettingsStore.Parse(json);
            _lastWrittenJson = json;
            Log.Info("Reloaded settings after an external edit.");
            if (SettingsSerializer.Serialize(Current) != json)
                SaveNow();
            Changed?.Invoke();
        }
        catch (SettingsFormatException ex)
        {
            // Keep running with the last good settings while the user fixes the file.
            Log.Error($"Ignoring invalid settings file edit: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        Flush();
    }
}
