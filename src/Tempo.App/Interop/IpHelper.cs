using System.Runtime.InteropServices;

namespace Tempo.App.Interop;

/// <summary>IP Helper queries that System.Net.NetworkInformation doesn't expose.</summary>
internal static class IpHelper
{
    private const byte HardwareInterfaceFlag = 0x01;
    private const byte FilterInterfaceFlag = 0x02;

    /// <summary>
    /// Whether the adapter is a real network card rather than a virtual one (Hyper-V / WSL switches, VPN taps, Wi-Fi Direct, filters).
    /// Null when Windows can't answer.
    /// </summary>
    public static bool? IsHardwareInterface(Guid adapterId)
    {
        if (ConvertInterfaceGuidToLuid(ref adapterId, out var luid) != 0)
            return null;
        var row = new MIB_IF_ROW2 { InterfaceLuid = luid };
        if (GetIfEntry2(ref row) != 0)
            return null;
        return (row.InterfaceAndOperStatusFlags & HardwareInterfaceFlag) != 0
            && (row.InterfaceAndOperStatusFlags & FilterInterfaceFlag) == 0;
    }

    [DllImport("iphlpapi.dll")]
    private static extern int ConvertInterfaceGuidToLuid(ref Guid interfaceGuid, out ulong interfaceLuid);

    [DllImport("iphlpapi.dll")]
    private static extern int GetIfEntry2(ref MIB_IF_ROW2 row);

    // netioapi.h; 1352 bytes on x64.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MIB_IF_ROW2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Alias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Description;
        public uint PhysicalAddressLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PhysicalAddress;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentPhysicalAddress;
        public uint Mtu;
        public uint Type;
        public int TunnelType;
        public int MediaType;
        public int PhysicalMediumType;
        public int AccessType;
        public int DirectionType;
        public byte InterfaceAndOperStatusFlags;
        public int OperStatus;
        public int AdminStatus;
        public int MediaConnectState;
        public Guid NetworkGuid;
        public int ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;
    }
}
