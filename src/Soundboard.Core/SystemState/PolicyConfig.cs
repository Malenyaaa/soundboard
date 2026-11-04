using System.Runtime.InteropServices;
using Soundboard.Core.Audio;

namespace Soundboard.Core.SystemState;

/// <summary>
/// Sets a Windows default audio endpoint through the undocumented IPolicyConfig interface
/// (the same COM object the Windows sound control panel uses). The app uses this for one
/// thing only: putting the default devices back to the IDs saved in a snapshot, when the
/// user asks for it. It is never used to make anything else a default.
/// </summary>
public sealed class PolicyConfigWriter : IDefaultEndpointWriter
{
    public void SetDefaultEndpoint(string deviceId, DeviceRoleKind role)
    {
        object? obj = null;
        try
        {
            obj = new PolicyConfigClient();
            var policy = (IPolicyConfig)obj;
            int hr = policy.SetDefaultEndpoint(deviceId, (int)DeviceCatalog.ToRole(role));
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
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
    /// Vtable layout of IPolicyConfig (Windows 7 and later). Only SetDefaultEndpoint is
    /// called; the entries before it exist to keep the slot positions and are never used.
    /// </summary>
    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int Slot01GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int Slot02GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int Slot03ResetDeviceFormat(IntPtr a);
        [PreserveSig] int Slot04SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
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
