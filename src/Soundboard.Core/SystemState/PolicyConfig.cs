using System.Runtime.InteropServices;
using Soundboard.Core.Audio;

namespace Soundboard.Core.SystemState;

/// <summary>
/// Sets a Windows default audio endpoint through the undocumented IPolicyConfig interface
/// (the same COM object the Windows sound control panel uses). The app uses this for one
/// thing only: putting the default devices back to the IDs saved in a snapshot – on request,
/// or by itself when Windows made the virtual cable a default. It is never used to make
/// anything else a default.
/// </summary>
public sealed class PolicyConfigWriter : IDefaultEndpointWriter
{
    public void SetDefaultEndpoint(string deviceId, DeviceRoleKind role) =>
        PolicyConfigInterop.Use(policy =>
        {
            int hr = policy.SetDefaultEndpoint(deviceId, (int)DeviceCatalog.ToRole(role));
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        });
}

/// <summary>The IPolicyConfig COM object of Windows (undocumented, stable since Windows 7).</summary>
internal static class PolicyConfigInterop
{
    /// <summary>Creates the object, runs <paramref name="action"/>, releases it.</summary>
    public static void Use(Action<IPolicyConfig> action)
    {
        object? obj = null;
        try
        {
            obj = new PolicyConfigClient();
            action((IPolicyConfig)obj);
        }
        finally
        {
            if (obj is not null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj);
        }
    }

    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient
    {
    }

    /// <summary>
    /// Vtable layout of IPolicyConfig (Windows 7 and later). Formats are WAVEFORMATEX(TENSIBLE)
    /// blocks; the ones Windows returns are freed with CoTaskMemFree. Slots that are never called
    /// exist to keep the positions.
    /// </summary>
    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);

        [PreserveSig]
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int useDefault, out IntPtr format);

        [PreserveSig] int Slot03ResetDeviceFormat(IntPtr a);

        [PreserveSig]
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);

        [PreserveSig] int Slot05GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int Slot06SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int Slot07GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int Slot08SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int Slot09GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int Slot10SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
    }
}
