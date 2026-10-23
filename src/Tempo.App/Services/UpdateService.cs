using CommunityToolkit.Mvvm.ComponentModel;
using Velopack;
using Velopack.Sources;

namespace Tempo.App.Services;

/// <summary>
/// Checks GitHub Releases for new versions and downloads them in the background.
/// A downloaded update is installed after Tempo exits through its normal shutdown path (which restores
/// the Windows taskbar first), or at the next launch if Tempo didn't get the chance.
/// </summary>
internal sealed partial class UpdateService : ObservableObject
{
    private const string RepositoryUrl = "https://github.com/cteerakit/tempo";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private bool _handedOff;

    public UpdateService()
    {
        Pending = _manager.IsInstalled ? _manager.UpdatePendingRestart : null;
        Status = !_manager.IsInstalled ? "Updates are only available when Tempo is installed with its setup program."
            : Pending is not null ? ReadyMessage(Pending)
            : "";
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string Version => _manager.CurrentVersion?.ToString() ?? typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "";

    [ObservableProperty]
    public partial string Status { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateReady))]
    public partial VelopackAsset? Pending { get; private set; }

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    public bool IsUpdateReady => Pending is not null;

    /// <summary>Checks shortly after startup, giving the network time to come up after sign-in.</summary>
    public async void CheckSoon()
    {
        await Task.Delay(StartupDelay);
        await CheckAsync();
    }

    public async Task CheckAsync()
    {
        if (!IsInstalled || IsChecking || _handedOff)
            return;
        IsChecking = true;
        try
        {
            Status = "Checking for updates…";
            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
            {
                Status = Pending is not null ? ReadyMessage(Pending) : $"Tempo is up to date. Last checked {DateTime.Now:t}.";
                return;
            }
            var target = update.TargetFullRelease;
            if (Pending?.Version != target.Version)
            {
                Status = $"Downloading Tempo {target.Version}…";
                await _manager.DownloadUpdatesAsync(update);
                Pending = target;
                Log.Info($"Downloaded Tempo {target.Version}.");
            }
            Status = ReadyMessage(target);
        }
        catch (Exception ex)
        {
            Log.Error("Update check failed", ex);
            Status = Pending is not null ? ReadyMessage(Pending) : "Couldn't check for updates. Tempo will try again later.";
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Hands the downloaded update to Velopack's updater, which waits for Tempo to exit, installs it,
    /// and optionally starts Tempo again. Call just before shutting down.
    /// </summary>
    public void ApplyAfterExit(bool restart)
    {
        if (Pending is null || _handedOff)
            return;
        try
        {
            _manager.WaitExitThenApplyUpdates(Pending, silent: true, restart: restart);
            _handedOff = true;
            Log.Info($"Installing Tempo {Pending.Version} after exit.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the updater", ex);
        }
    }

    private static string ReadyMessage(VelopackAsset asset) => $"Tempo {asset.Version} is ready. It installs when Tempo restarts.";
}
