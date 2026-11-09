using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tempo.App.Interop;
using Tempo.App.Shell;
using Windows.Devices.Radios;
using Windows.Foundation;
using Windows.Networking.Connectivity;

namespace Tempo.App.Widgets;

internal sealed class NetworkSettings
{
    [DisplayName("Show network name")]
    public bool ShowName { get; set; }

    [DisplayName("Show transfer speed"), Description("Live download and upload rates next to the icon")]
    public bool ShowSpeed { get; set; }
}

/// <summary>
/// Status of the physical Wi-Fi and Ethernet adapters: which one carries the internet, signal, addresses and live throughput,
/// plus a Wi-Fi radio switch. Virtual adapters (Hyper-V, WSL, VPN taps) are left out.
/// </summary>
internal sealed partial class NetworkWidget(WidgetContext context, NetworkSettings settings) : WidgetViewModelBase(context)
{
    private const string OfflineGlyph = "";
    private const string EthernetGlyph = "";

    private readonly Dictionary<string, bool> _isPhysical = [];
    private Radio? _wifiRadio;
    private TypedEventHandler<Radio, object>? _radioHandler;
    private bool _applyingRadioState;

    public NetworkSettings Settings { get; } = settings;

    public ObservableCollection<NetworkAdapterItem> Adapters { get; } = [];

    public static IRelayCommand<string> CopyCommand { get; } = new RelayCommand<string>(CopyToClipboard);

    [ObservableProperty]
    public partial string Glyph { get; set; } = OfflineGlyph;

    /// <summary>The SSID or network profile name of the connection carrying traffic.</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>One-line summary for the tooltip.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "Not connected";

    [ObservableProperty]
    public partial string Headline { get; set; } = "Not connected";

    [ObservableProperty]
    public partial string Subline { get; set; } = "";

    [ObservableProperty]
    public partial string DownRate { get; set; } = "";

    [ObservableProperty]
    public partial string UpRate { get; set; } = "";

    [ObservableProperty]
    public partial bool HasWifi { get; set; }

    [ObservableProperty]
    public partial bool HasEthernet { get; set; }

    [ObservableProperty]
    public partial bool HasWifiRadio { get; set; }

    [ObservableProperty]
    public partial bool IsWifiOn { get; set; } = true;

    public override void Start()
    {
        NetworkStatusChangedEventHandler statusHandler = _ => Post(Refresh);
        NetworkInformation.NetworkStatusChanged += statusHandler;
        Own(() => NetworkInformation.NetworkStatusChanged -= statusHandler);
        NetworkAddressChangedEventHandler addressHandler = (_, _) => Post(Refresh);
        NetworkChange.NetworkAddressChanged += addressHandler;
        Own(() => NetworkChange.NetworkAddressChanged -= addressHandler);

        // Signal strength and SSID changes don't raise either event.
        Every(TimeSpan.FromSeconds(15), Refresh);
        Every(TimeSpan.FromSeconds(1), SampleThroughput);
        _ = AttachWifiRadioAsync();
    }

    protected override void OnFlyoutOpenChanged(bool isOpen)
    {
        if (isOpen) Refresh();
    }

    partial void OnIsWifiOnChanged(bool value)
    {
        if (!_applyingRadioState && _wifiRadio is not null)
            _ = SetWifiRadioAsync(value);
    }

    /// <summary>The Windows network list, for joining a new Wi-Fi network.</summary>
    [RelayCommand]
    private void ShowAvailableNetworks() => OpenSettings("ms-availablenetworks:");

    [RelayCommand]
    private void OpenWifiSettings() => OpenSettings("ms-settings:network-wifi");

    [RelayCommand]
    private void OpenEthernetSettings() => OpenSettings("ms-settings:network-ethernet");

    [RelayCommand]
    private void OpenNetworkSettings() => OpenSettings("ms-settings:network-advancedsettings");

    private void OpenSettings(string uri)
    {
        IsFlyoutOpen = false;
        ShellActions.Launch(uri);
    }

    public override void Dispose()
    {
        IsFlyoutOpen = false;
        if (_wifiRadio is not null && _radioHandler is not null)
            _wifiRadio.StateChanged -= _radioHandler;
        base.Dispose();
    }

    private static void Post(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    private static void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Log.Error("Copying to the clipboard failed", ex);
        }
    }

    // ---- Wi-Fi radio ----

    private async Task AttachWifiRadioAsync()
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
                return;
            var radio = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            if (radio is null)
                return;
            _wifiRadio = radio;
            _radioHandler = (_, _) => Post(() =>
            {
                ApplyRadioState();
                Refresh();
            });
            radio.StateChanged += _radioHandler;
            ApplyRadioState();
            HasWifiRadio = true;
        }
        catch (Exception ex)
        {
            Log.Error("Wi-Fi radio lookup failed", ex);
        }
    }

    private void ApplyRadioState()
    {
        if (_wifiRadio is null) return;
        _applyingRadioState = true;
        IsWifiOn = _wifiRadio.State == RadioState.On;
        _applyingRadioState = false;
    }

    private async Task SetWifiRadioAsync(bool on)
    {
        try
        {
            var result = await _wifiRadio!.SetStateAsync(on ? RadioState.On : RadioState.Off);
            if (result != RadioAccessStatus.Allowed)
                Log.Info($"Turning Wi-Fi {(on ? "on" : "off")} was refused: {result}");
        }
        catch (Exception ex)
        {
            Log.Error("Switching the Wi-Fi radio failed", ex);
        }
        ApplyRadioState();
    }

    // ---- Adapter status ----

    private void Refresh()
    {
        try
        {
            var profiles = new Dictionary<Guid, ConnectionProfile>();
            foreach (var profile in NetworkInformation.GetConnectionProfiles())
            {
                if (profile.NetworkAdapter is not { } networkAdapter) continue;
                var id = networkAdapter.NetworkAdapterId;
                if (!profiles.TryGetValue(id, out var existing) || Level(profile) > Level(existing))
                    profiles[id] = profile;
            }
            var internetAdapter = NetworkInformation.GetInternetConnectionProfile()?.NetworkAdapter?.NetworkAdapterId;

            var current = new List<NetworkAdapterItem>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!Guid.TryParse(adapter.Id, out var guid) || !IsPhysical(adapter, guid))
                    continue;
                var item = Adapters.FirstOrDefault(a => a.Id == adapter.Id)
                    ?? new NetworkAdapterItem(adapter.Id, adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
                Update(item, adapter, profiles.GetValueOrDefault(guid), guid == internetAdapter);
                current.Add(item);
            }

            // Whatever carries the internet first, then other live connections, then idle adapters; Wi-Fi before Ethernet within each.
            current = [.. current.OrderByDescending(a => a.IsPrimary).ThenByDescending(a => a.IsConnected).ThenByDescending(a => a.IsWifi)];
            SyncAdapters(current);

            HasWifi = current.Any(a => a.IsWifi);
            HasEthernet = current.Any(a => !a.IsWifi);
            UpdateSummary(current.FirstOrDefault(a => a.IsPrimary) ?? current.FirstOrDefault(a => a.IsConnected));
        }
        catch (Exception ex)
        {
            Log.Error("Network status query failed", ex);
        }
    }

    private void SyncAdapters(List<NetworkAdapterItem> ordered)
    {
        for (var i = Adapters.Count - 1; i >= 0; i--)
            if (!ordered.Contains(Adapters[i]))
                Adapters.RemoveAt(i);
        for (var i = 0; i < ordered.Count; i++)
        {
            var at = Adapters.IndexOf(ordered[i]);
            if (at < 0) Adapters.Insert(i, ordered[i]);
            else if (at != i) Adapters.Move(at, i);
        }
    }

    private void UpdateSummary(NetworkAdapterItem? primary)
    {
        if (primary is null)
        {
            (Glyph, Name) = (OfflineGlyph, "");
            Headline = HasWifi && HasWifiRadio && !IsWifiOn ? "Wi-Fi is off" : "Not connected";
            Subline = HasEthernet ? "Plug in a cable or join a Wi-Fi network" : "Join a Wi-Fi network to get online";
            Status = Headline;
            return;
        }
        Glyph = primary.Glyph;
        Name = primary.NetworkName;
        Headline = primary.NetworkName;
        Subline = $"{primary.State} · {(primary.IsWifi ? "Wi-Fi" : "Ethernet")}";
        Status = $"{primary.NetworkName} — {primary.State.ToLowerInvariant()}";
    }

    private bool IsPhysical(NetworkInterface adapter, Guid guid)
    {
        if (adapter.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit
            or NetworkInterfaceType.Wireless80211))
            return false;
        // Bluetooth PAN shows up as a hardware Ethernet adapter but isn't one anyone means by "Ethernet".
        if (adapter.Description.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!_isPhysical.TryGetValue(adapter.Id, out var physical))
        {
            physical = IpHelper.IsHardwareInterface(guid) ?? !LooksVirtual(adapter.Description);
            _isPhysical[adapter.Id] = physical;
        }
        return physical;
    }

    private static bool LooksVirtual(string description) =>
        new[] { "Virtual", "Hyper-V", "VMware", "VirtualBox", "TAP-", "Wintun", "WireGuard", "Loopback", "Miniport" }
            .Any(word => description.Contains(word, StringComparison.OrdinalIgnoreCase));

    private void Update(NetworkAdapterItem item, NetworkInterface adapter, ConnectionProfile? profile, bool carriesInternet)
    {
        item.Adapter = adapter;
        item.Title = adapter.Name;
        item.Description = adapter.Description;
        item.IsPrimary = carriesInternet;
        item.IsConnected = adapter.OperationalStatus == OperationalStatus.Up;

        if (!item.IsConnected)
        {
            item.Glyph = item.IsWifi ? "" : EthernetGlyph;
            item.NetworkName = "";
            item.State = item.IsWifi ? (HasWifiRadio && !IsWifiOn ? "Turned off" : "Not connected") : "Cable unplugged";
            item.StatusLine = item.State;
            item.Details = [];
            item.Activity = "";
            item.ResetThroughput();
            return;
        }

        var level = profile is null ? NetworkConnectivityLevel.None : Level(profile);
        item.State = level switch
        {
            NetworkConnectivityLevel.InternetAccess => "Connected",
            NetworkConnectivityLevel.ConstrainedInternetAccess => "Sign-in required",
            NetworkConnectivityLevel.LocalAccess => "No internet",
            _ => "Connecting…",
        };

        var details = new List<NetworkDetail>();
        if (item.IsWifi)
        {
            var bars = profile?.GetSignalBars();
            item.Glyph = bars switch { null => "", <= 1 => "", 2 => "", 3 => "", _ => "" };
            item.NetworkName = profile?.WlanConnectionProfileDetails?.GetConnectedSsid() is { Length: > 0 } ssid ? ssid : profile?.ProfileName ?? adapter.Name;
            if (bars is { } b)
                details.Add(new("Signal", $"{SignalQuality(b)} ({b} of 5 bars)"));
            if (SecurityName(profile) is { } security)
                details.Add(new("Security", security));
        }
        else
        {
            item.Glyph = EthernetGlyph;
            item.NetworkName = profile?.ProfileName is { Length: > 0 } name ? name : adapter.Name;
        }
        item.StatusLine = $"{item.NetworkName} · {item.State}";

        if (adapter.Speed > 0)
            details.Add(new("Link speed", FormatLinkSpeed(adapter.Speed)));

        var ip = adapter.GetIPProperties();
        var ipv4 = ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).ToList();
        if (ipv4.Count > 0)
            details.Add(new("IPv4", string.Join(", ", ipv4), CanCopy: true));
        var ipv6 = ip.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal);
        if (ipv6 is not null)
            details.Add(new("IPv6", ipv6.Address.ToString(), CanCopy: true));
        var gateway = ip.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(System.Net.IPAddress.Any));
        if (gateway is not null)
            details.Add(new("Gateway", gateway.ToString(), CanCopy: true));
        var dns = ip.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Take(2).Select(a => a.ToString()).ToList();
        if (dns.Count > 0)
            details.Add(new("DNS", string.Join(", ", dns), CanCopy: true));
        if (FormatMac(adapter.GetPhysicalAddress()) is { } mac)
            details.Add(new("MAC", mac, CanCopy: true));

        if (!item.Details.SequenceEqual(details))
            item.Details = details;
    }

    private static NetworkConnectivityLevel Level(ConnectionProfile profile)
    {
        try
        {
            return profile.GetNetworkConnectivityLevel();
        }
        catch
        {
            return NetworkConnectivityLevel.None;
        }
    }

    private static string SignalQuality(byte bars) => bars switch { >= 4 => "Excellent", 3 => "Good", 2 => "Fair", _ => "Weak" };

    private static string? SecurityName(ConnectionProfile? profile)
    {
        try
        {
            // Matched by name: newer authentication types aren't in every SDK's enum.
            return profile?.NetworkSecuritySettings?.NetworkAuthenticationType.ToString() switch
            {
                null or "None" or "Unknown" => null,
                "Open80211" => "Open",
                "SharedKey80211" => "WEP",
                "Wpa" => "WPA-Enterprise",
                "WpaPsk" => "WPA-Personal",
                "Rsna" => "WPA2-Enterprise",
                "RsnaPsk" => "WPA2-Personal",
                "Wpa3Sae" => "WPA3-Personal",
                "Wpa3" or "Wpa3Enterprise" or "Wpa3Enterprise192Bits" => "WPA3-Enterprise",
                "Owe" => "Enhanced Open",
                var other => other,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string FormatLinkSpeed(long bitsPerSecond) => bitsPerSecond >= 1_000_000_000
        ? $"{bitsPerSecond / 1e9:0.#} Gbps"
        : $"{bitsPerSecond / 1e6:0} Mbps";

    private static string? FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? null : string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    // ---- Throughput ----

    private void SampleThroughput()
    {
        // Only worth the work when someone can see it.
        if (!Settings.ShowSpeed && !IsFlyoutOpen)
        {
            foreach (var item in Adapters) item.ResetThroughput();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        double totalDown = 0, totalUp = 0;
        foreach (var item in Adapters)
        {
            if (!item.IsConnected || item.Adapter is null) continue;
            long received, sent;
            try
            {
                var stats = item.Adapter.GetIPStatistics();
                (received, sent) = (stats.BytesReceived, stats.BytesSent);
            }
            catch
            {
                continue;
            }
            if (item.LastSample != 0)
            {
                var seconds = Stopwatch.GetElapsedTime(item.LastSample, now).TotalSeconds;
                if (seconds > 0)
                {
                    var down = Math.Max(0, received - item.LastReceived) / seconds;
                    var up = Math.Max(0, sent - item.LastSent) / seconds;
                    item.Activity = $"↓ {FormatRate(down)}    ↑ {FormatRate(up)}";
                    totalDown += down;
                    totalUp += up;
                }
            }
            (item.LastSample, item.LastReceived, item.LastSent) = (now, received, sent);
        }
        DownRate = FormatRate(totalDown);
        UpRate = FormatRate(totalUp);
    }

    private static string FormatRate(double bytesPerSecond)
    {
        var kb = bytesPerSecond / 1024;
        if (kb < 1000) return $"{kb:0} KB/s";
        var mb = kb / 1024;
        return mb < 100 ? $"{mb:0.0} MB/s" : $"{mb:0} MB/s";
    }
}

internal sealed partial class NetworkAdapterItem(string id, bool isWifi) : ObservableObject
{
    public string Id { get; } = id;

    public bool IsWifi { get; } = isWifi;

    internal NetworkInterface? Adapter { get; set; }

    internal long LastSample { get; set; }

    internal long LastReceived { get; set; }

    internal long LastSent { get; set; }

    /// <summary>Carries the internet connection (Windows' preferred route).</summary>
    public bool IsPrimary { get; set; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string Glyph { get; set; } = "";

    /// <summary>The Windows connection name, e.g. "Wi-Fi" or "Ethernet 2".</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "";

    /// <summary>The hardware, e.g. "Intel(R) Wi-Fi 6E AX211 160MHz".</summary>
    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial string NetworkName { get; set; } = "";

    [ObservableProperty]
    public partial string State { get; set; } = "";

    [ObservableProperty]
    public partial string StatusLine { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<NetworkDetail> Details { get; set; } = [];

    [ObservableProperty]
    public partial string Activity { get; set; } = "";

    public void ResetThroughput()
    {
        LastSample = 0;
        Activity = "";
    }
}

internal sealed record NetworkDetail(string Label, string Value, bool CanCopy = false);
