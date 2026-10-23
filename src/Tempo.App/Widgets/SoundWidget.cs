using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using Tempo.App.Interop;
using Tempo.App.Shell;

namespace Tempo.App.Widgets;

internal sealed class SoundSettings
{
    [DisplayName("Show percentage")]
    public bool ShowPercent { get; set; }

    [DisplayName("Scroll step (%)"), Range(1, 20)]
    public double ScrollStep { get; set; } = 2;
}

/// <summary>One selectable endpoint in the flyout's output or input dropdown.</summary>
internal sealed record AudioDeviceItem(string Id, string Name, bool IsDefault);

/// <summary>
/// Output volume via Core Audio, plus switching the default output and input devices.
/// Scroll to adjust the volume, click for the slider and device dropdowns.
/// </summary>
internal sealed partial class SoundWidget(WidgetContext context, SoundSettings settings) : WidgetViewModelBase(context)
{
    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _notifications;
    private MMDevice? _device;
    private bool _syncingFromDevice;
    private bool _fillingDeviceLists;

    public SoundSettings Settings { get; } = settings;

    public ObservableCollection<AudioDeviceItem> OutputDevices { get; } = [];

    public ObservableCollection<AudioDeviceItem> InputDevices { get; } = [];

    /// <summary>The selected item in the output dropdown; picking another makes it the default.</summary>
    [ObservableProperty]
    public partial AudioDeviceItem? SelectedOutput { get; set; }

    /// <summary>The selected item in the input dropdown; picking another makes it the default.</summary>
    [ObservableProperty]
    public partial AudioDeviceItem? SelectedInput { get; set; }

    [ObservableProperty]
    public partial double Level { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial bool HasDevice { get; set; }

    [ObservableProperty]
    public partial string DeviceName { get; set; } = "";

    public string Glyph => !HasDevice ? ""
        : IsMuted || Level < 0.5 ? ""
        : Level < 34 ? ""
        : Level < 67 ? ""
        : "";

    partial void OnLevelChanged(double value)
    {
        OnPropertyChanged(nameof(Glyph));
        if (!_syncingFromDevice && _device is not null)
            TryAudio(() => _device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value / 100, 0, 1));
    }

    partial void OnIsMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(Glyph));
        if (!_syncingFromDevice && _device is not null)
            TryAudio(() => _device.AudioEndpointVolume.Mute = value);
    }

    partial void OnHasDeviceChanged(bool value) => OnPropertyChanged(nameof(Glyph));

    partial void OnSelectedOutputChanged(AudioDeviceItem? value) => SelectDevice(value);

    partial void OnSelectedInputChanged(AudioDeviceItem? value) => SelectDevice(value);

    public override void Start()
    {
        TryAudio(() =>
        {
            _enumerator = new MMDeviceEnumerator();
            _notifications = _enumerator.CreateNotificationClient(useSynchronizationContext: true);
            _notifications.DefaultDeviceChanged += (_, e) =>
            {
                if (e.Flow == DataFlow.Render)
                    AttachDefaultOutput();
                RefreshDeviceLists();
            };
            _notifications.DeviceStateChanged += (_, _) => Refresh();
            _notifications.DeviceAdded += (_, _) => RefreshDeviceLists();
            _notifications.DeviceRemoved += (_, _) => RefreshDeviceLists();
            Refresh();
        });
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    [RelayCommand]
    private void Scroll(int delta) => Level = Math.Clamp(Level + Math.Sign(delta) * Settings.ScrollStep, 0, 100);

    private void SelectDevice(AudioDeviceItem? item)
    {
        // Refilling the lists clears and restores the selection; only a user's pick switches devices.
        if (_fillingDeviceLists || item is null || item.IsDefault)
            return;
        TryAudio(() => AudioPolicy.SetDefaultEndpoint(item.Id));
        // The change notification normally follows, but don't wait on it to update the lists. Deferred so
        // the dropdown isn't refilled while it is still applying the selection.
        Application.Current?.Dispatcher.BeginInvoke(Refresh);
    }

    [RelayCommand]
    private void OpenSoundSettings()
    {
        IsFlyoutOpen = false;
        ShellActions.OpenSoundSettings();
    }

    private void Refresh()
    {
        AttachDefaultOutput();
        RefreshDeviceLists();
    }

    private void AttachDefaultOutput()
    {
        DetachDevice();
        if (_enumerator is null)
            return;
        TryAudio(() =>
        {
            if (!_enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var device) || device is null)
            {
                HasDevice = false;
                DeviceName = "No output device";
                return;
            }
            _device = device;
            _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
            DeviceName = device.FriendlyName;
            HasDevice = true;
            SyncFromDevice(device.AudioEndpointVolume.MasterVolumeLevelScalar, device.AudioEndpointVolume.Mute);
        });
    }

    private void RefreshDeviceLists()
    {
        if (_enumerator is null)
            return;
        _fillingDeviceLists = true;
        try
        {
            TryAudio(() =>
            {
                SelectedOutput = Fill(OutputDevices, DataFlow.Render);
                SelectedInput = Fill(InputDevices, DataFlow.Capture);
            });
        }
        finally
        {
            _fillingDeviceLists = false;
        }
    }

    /// <summary>Refills <paramref name="target"/> with the active endpoints and returns the default one.</summary>
    private AudioDeviceItem? Fill(ObservableCollection<AudioDeviceItem> target, DataFlow flow)
    {
        string? defaultId = null;
        if (_enumerator!.TryGetDefaultAudioEndpoint(flow, Role.Multimedia, out var defaultDevice) && defaultDevice is not null)
        {
            defaultId = defaultDevice.ID;
            defaultDevice.Dispose();
        }

        var items = new List<AudioDeviceItem>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
                items.Add(new AudioDeviceItem(device.ID, device.FriendlyName, device.ID == defaultId));
        }

        target.Clear();
        foreach (var item in items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
            target.Add(item);
        return items.FirstOrDefault(i => i.IsDefault);
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data) =>
        // Raised on an audio worker thread that holds a lock: hand off and return immediately.
        Application.Current?.Dispatcher.BeginInvoke(() => SyncFromDevice(data.MasterVolume, data.Muted));

    private void SyncFromDevice(float scalar, bool muted)
    {
        _syncingFromDevice = true;
        try
        {
            var level = Math.Round(scalar * 100);
            // Ignore echoes of our own writes so a dragged slider doesn't jitter.
            if (Math.Abs(level - Level) >= 0.5)
                Level = level;
            IsMuted = muted;
        }
        finally
        {
            _syncingFromDevice = false;
        }
    }

    private void DetachDevice()
    {
        if (_device is null)
            return;
        TryAudio(() => _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification);
        _device.Dispose();
        _device = null;
    }

    public override void Dispose()
    {
        DetachDevice();
        _notifications?.Dispose();
        _enumerator?.Dispose();
        base.Dispose();
    }

    private static void TryAudio(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("Core Audio call failed", ex);
        }
    }
}
