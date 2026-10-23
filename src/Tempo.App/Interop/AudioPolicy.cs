using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Tempo.App.Interop;

/// <summary>
/// Windows has no public API for changing the default audio endpoint. This is the undocumented
/// <c>IPolicyConfig</c> interface that the Sound settings page and every audio switcher use; it has been
/// stable since Windows 7.
/// </summary>
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    // Only SetDefaultEndpoint is used; the rest are declared to keep the native vtable order.
    [PreserveSig] int GetMixFormat(nint deviceId, nint format);
    [PreserveSig] int GetDeviceFormat(nint deviceId, int isDefault, nint format);
    [PreserveSig] int ResetDeviceFormat(nint deviceId);
    [PreserveSig] int SetDeviceFormat(nint deviceId, nint endpointFormat, nint mixFormat);
    [PreserveSig] int GetProcessingPeriod(nint deviceId, int isDefault, nint defaultPeriod, nint minimumPeriod);
    [PreserveSig] int SetProcessingPeriod(nint deviceId, nint period);
    [PreserveSig] int GetShareMode(nint deviceId, nint mode);
    [PreserveSig] int SetShareMode(nint deviceId, nint mode);
    [PreserveSig] int GetPropertyValue(nint deviceId, int bFxStore, nint key, nint value);
    [PreserveSig] int SetPropertyValue(nint deviceId, int bFxStore, nint key, nint value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, Role role);
    [PreserveSig] int SetEndpointVisibility(nint deviceId, int visible);
}

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClient;

internal static class AudioPolicy
{
    /// <summary>Makes the endpoint the default for every role, as the Windows Sound settings page does.</summary>
    public static void SetDefaultEndpoint(string deviceId)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            foreach (var role in new[] { Role.Console, Role.Multimedia, Role.Communications })
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }
}
