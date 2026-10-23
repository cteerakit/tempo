using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tempo.App.Shell;
using Windows.Media.Control;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using SessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace Tempo.App.Widgets;

internal sealed class MediaSettings
{
    [DisplayName("Minimized")]
    [Description("Show only the cover and play/pause button in the bar.")]
    public bool Minimized { get; set; }

    [DisplayName("Show artwork")]
    public bool ShowArtwork { get; set; } = true;

    [DisplayName("Show playback buttons")]
    public bool ShowControls { get; set; } = true;

    [DisplayName("Maximum width"), Range(80, 600)]
    public double MaxWidth { get; set; } = 220;

    [DisplayName("Hide when nothing is playing")]
    public bool HideWhenIdle { get; set; } = true;
}

/// <summary>Now playing, from whichever app Windows considers the current media session.</summary>
internal sealed partial class MediaWidget(WidgetContext context, MediaSettings settings) : WidgetViewModelBase(context)
{
    private SessionManager? _manager;
    private Session? _session;
    private int _artworkVersion;

    public MediaSettings Settings { get; } = settings;

    [ObservableProperty]
    public partial bool HasSession { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string Artist { get; set; } = "";

    [ObservableProperty]
    public partial ImageSource? Artwork { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseGlyph))]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial string SourceApp { get; set; } = "";

    public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768";

    public string Summary => string.IsNullOrEmpty(Artist) ? Title : $"{Title} · {Artist}";

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(Summary));

    partial void OnArtistChanged(string value) => OnPropertyChanged(nameof(Summary));

    partial void OnHasSessionChanged(bool value) => UpdateVisibility();

    public override void Start()
    {
        UpdateVisibility();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _manager = await SessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            Own(() => _manager.CurrentSessionChanged -= OnCurrentSessionChanged);
            AttachSession(_manager.GetCurrentSession());
        }
        catch (Exception ex)
        {
            Log.Error("Media session manager unavailable", ex);
        }
    }

    [RelayCommand]
    private Task PlayPause() => Run(async session =>
    {
        var playback = session.GetPlaybackInfo();
        var accepted = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            ? await session.TryPauseAsync()
            : await session.TryPlayAsync();

        if (session == _session)
            UpdatePlayback(session);
        return accepted;
    });

    [RelayCommand]
    private Task Next() => Run(s => s.TrySkipNextAsync().AsTask());

    [RelayCommand]
    private Task Previous() => Run(s => s.TrySkipPreviousAsync().AsTask());

    [RelayCommand]
    private void OpenSourceApp()
    {
        IsFlyoutOpen = false;
        if (_session?.SourceAppUserModelId is { Length: > 0 } aumid)
            ShellActions.Launch($@"shell:AppsFolder\{aumid}");
    }

    private void OnCurrentSessionChanged(SessionManager sender, CurrentSessionChangedEventArgs args) =>
        OnUi(() => AttachSession(sender.GetCurrentSession()));

    private void AttachSession(Session? session)
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _session = session;
        HasSession = session is not null;
        if (session is null)
        {
            (Title, Artist, Artwork, IsPlaying, SourceApp) = ("", "", null, false, "");
            return;
        }

        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        SourceApp = session.SourceAppUserModelId ?? "";
        UpdatePlayback(session);
        _ = UpdateMediaPropertiesAsync(session);
    }

    private void OnMediaPropertiesChanged(Session sender, MediaPropertiesChangedEventArgs args) =>
        OnUi(() => _ = UpdateMediaPropertiesAsync(sender));

    private void OnPlaybackInfoChanged(Session sender, PlaybackInfoChangedEventArgs args) =>
        OnUi(() => UpdatePlayback(sender));

    private void UpdatePlayback(Session session)
    {
        try
        {
            IsPlaying = session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception ex)
        {
            Log.Error("Could not read playback info", ex);
        }
    }

    private async Task UpdateMediaPropertiesAsync(Session session)
    {
        var version = ++_artworkVersion;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (version != _artworkVersion || session != _session || props is null)
                return;

            Title = props.Title ?? "";
            Artist = props.Artist ?? "";

            ImageSource? artwork = null;
            if (props.Thumbnail is not null)
            {
                using var winrtStream = await props.Thumbnail.OpenReadAsync();
                using var source = winrtStream.AsStreamForRead();
                var buffer = new MemoryStream();
                await source.CopyToAsync(buffer);
                buffer.Position = 0;

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 160;
                image.StreamSource = buffer;
                image.EndInit();
                image.Freeze();
                artwork = image;
            }
            if (version == _artworkVersion)
                Artwork = artwork;
        }
        catch (Exception ex)
        {
            // Some players report a session before their metadata is ready; the next change event retries.
            Log.Error("Could not read media properties", ex);
        }
    }

    private async Task Run(Func<Session, Task<bool>> action)
    {
        if (_session is null)
            return;
        try
        {
            await action(_session);
        }
        catch (Exception ex)
        {
            Log.Error("Media command failed", ex);
        }
    }

    private void UpdateVisibility() => IsVisible = HasSession || !Settings.HideWhenIdle;

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    public override void Dispose()
    {
        AttachSession(null);
        base.Dispose();
    }
}
