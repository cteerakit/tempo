using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tempo.App.Shell;
using Windows.Networking.Connectivity;

namespace Tempo.App.Widgets;

internal sealed class NetworkSettings
{
    [DisplayName("Show network name")]
    public bool ShowName { get; set; }
}

internal sealed partial class NetworkWidget(WidgetContext context, NetworkSettings settings) : WidgetViewModelBase(context)
{
    public NetworkSettings Settings { get; } = settings;

    [ObservableProperty]
    public partial string Glyph { get; set; } = "\uF384";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    public override void Start()
    {
        NetworkStatusChangedEventHandler handler = _ => Application.Current?.Dispatcher.BeginInvoke(Refresh);
        NetworkInformation.NetworkStatusChanged += handler;
        Own(() => NetworkInformation.NetworkStatusChanged -= handler);
        // Signal strength and SSID changes don't raise NetworkStatusChanged.
        Every(TimeSpan.FromSeconds(15), Refresh);
    }

    public ObservableCollection<NetworkConnection> Connections { get; } = [];

    protected override void OnFlyoutOpenChanged(bool isOpen)
    {
        if (isOpen) Refresh();
    }

    [RelayCommand]
    private void OpenNetworkSettings()
    {
        IsFlyoutOpen = false;
        ShellActions.Launch("ms-settings:network");
    }

    [RelayCommand]
    private void ManageWifi()
    {
        IsFlyoutOpen = false;
        ShellActions.Launch("ms-settings:network-wifi");
    }

    public override void Dispose()
    {
        IsFlyoutOpen = false;
        base.Dispose();
    }

    private void Refresh()
    {
        try
        {
            Connections.Clear();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                .Where(a => a.OperationalStatus == OperationalStatus.Up && a.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var addresses = adapter.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString());
                Connections.Add(new NetworkConnection(adapter.Name,
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : adapter.NetworkInterfaceType.ToString(),
                    string.Join(" · ", addresses)));
            }
            var profile = NetworkInformation.GetInternetConnectionProfile();
            var level = profile?.GetNetworkConnectivityLevel() ?? NetworkConnectivityLevel.None;
            if (profile is null || level == NetworkConnectivityLevel.None)
            {
                (Glyph, Name, Status) = ("\uF384", "", "Not connected");
                return;
            }

            var limited = level != NetworkConnectivityLevel.InternetAccess;
            if (profile.IsWlanConnectionProfile)
            {
                var bars = profile.GetSignalBars() ?? 4;
                Glyph = bars switch { <= 1 => "\uE872", 2 => "\uE873", 3 => "\uE874", _ => "\uE701" };
                Name = profile.WlanConnectionProfileDetails?.GetConnectedSsid() ?? profile.ProfileName;
            }
            else if (profile.IsWwanConnectionProfile)
            {
                Glyph = "\uEC3B";
                Name = profile.ProfileName;
            }
            else
            {
                Glyph = "\uE839";
                Name = profile.ProfileName;
            }
            Status = limited ? $"{Name} — no internet" : $"{Name} — connected";
        }
        catch (Exception ex)
        {
            Log.Error("Network status query failed", ex);
        }
    }
}

internal sealed record NetworkConnection(string Name, string Kind, string Address);
