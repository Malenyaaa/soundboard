using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using Soundboard.Core.Audio;

namespace Soundboard.Core.SystemState;

/// <summary>
/// The two formats of an endpoint as Windows keeps them: the device (endpoint) format chosen under
/// Sound → Properties → Advanced, and the shared-mode mix format. Raw WAVEFORMATEX(TENSIBLE) bytes.
/// </summary>
public sealed record EndpointFormats(byte[] Device, byte[] Mix)
{
    public int SampleRate => WaveFormatBytes.Rate(Device);
}

/// <summary>An endpoint as Windows names it now.</summary>
public sealed record EndpointIdentity(string Name, DeviceFlow Flow, DeviceStatus Status);

/// <summary>Reads and writes endpoint formats. Implemented over IPolicyConfig; replaceable in tests.</summary>
public interface IDeviceFormatApi
{
    /// <summary>Name, direction and state of an endpoint; null if Windows does not know it.</summary>
    EndpointIdentity? Find(string deviceId);

    /// <summary>The current formats; null if they cannot be read (endpoint not active).</summary>
    EndpointFormats? Read(string deviceId);

    /// <summary>Sets both formats. Throws when Windows refuses.</summary>
    void Write(string deviceId, byte[] deviceFormat, byte[] mixFormat);
}

/// <summary>WAVEFORMATEX fields in raw bytes (little endian).</summary>
public static class WaveFormatBytes
{
    private const int HeaderSize = 18; // up to and including cbSize

    public static bool IsValid(byte[]? f) =>
        f is { Length: >= HeaderSize } && f.Length >= HeaderSize + BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(16))
        && Channels(f) > 0 && Rate(f) > 0 && BlockAlign(f) > 0;

    public static int Channels(byte[] f) => BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(2));

    public static int Rate(byte[] f) => (int)BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(4));

    public static int BlockAlign(byte[] f) => BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(12));

    public static int Bits(byte[] f) => BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(14));

    /// <summary>A copy with another sample rate; channels, bit depth, sample type and channel mask stay.</summary>
    public static byte[] WithRate(byte[] f, int rate)
    {
        var copy = (byte[])f.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(4), (uint)rate);
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(8), (uint)(rate * BlockAlign(f)));
        return copy;
    }
}

/// <summary>The real thing: IPolicyConfig for the formats, the endpoint enumerator for name and state.</summary>
public sealed class PolicyConfigFormatApi : IDeviceFormatApi
{
    public EndpointIdentity? Find(string deviceId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var dev = enumerator.GetDevice(deviceId);
            var status = dev.State switch
            {
                DeviceState.Active => DeviceStatus.Active,
                DeviceState.Disabled => DeviceStatus.Disabled,
                DeviceState.NotPresent => DeviceStatus.NotPresent,
                DeviceState.Unplugged => DeviceStatus.Unplugged,
                _ => DeviceStatus.Unknown,
            };
            string name;
            try { name = dev.FriendlyName; }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or KeyNotFoundException) { name = ""; }
            return new EndpointIdentity(name, dev.DataFlow == DataFlow.Capture ? DeviceFlow.Capture : DeviceFlow.Render, status);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return null;
        }
    }

    public EndpointFormats? Read(string deviceId)
    {
        EndpointFormats? result = null;
        PolicyConfigInterop.Use(policy =>
        {
            if (policy.GetDeviceFormat(deviceId, 0, out var device) != 0 || device == IntPtr.Zero) return;
            try
            {
                if (policy.GetMixFormat(deviceId, out var mix) != 0 || mix == IntPtr.Zero) return;
                try { result = new EndpointFormats(Copy(device), Copy(mix)); }
                finally { Marshal.FreeCoTaskMem(mix); }
            }
            finally { Marshal.FreeCoTaskMem(device); }
        });
        return result is not null && WaveFormatBytes.IsValid(result.Device) && WaveFormatBytes.IsValid(result.Mix) ? result : null;
    }

    public void Write(string deviceId, byte[] deviceFormat, byte[] mixFormat)
    {
        if (!WaveFormatBytes.IsValid(deviceFormat) || !WaveFormatBytes.IsValid(mixFormat))
            throw new ArgumentException("Not a valid wave format.");
        var pDevice = Marshal.AllocCoTaskMem(deviceFormat.Length);
        var pMix = Marshal.AllocCoTaskMem(mixFormat.Length);
        try
        {
            Marshal.Copy(deviceFormat, 0, pDevice, deviceFormat.Length);
            Marshal.Copy(mixFormat, 0, pMix, mixFormat.Length);
            PolicyConfigInterop.Use(policy =>
            {
                int hr = policy.SetDeviceFormat(deviceId, pDevice, pMix);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            });
        }
        finally
        {
            Marshal.FreeCoTaskMem(pDevice);
            Marshal.FreeCoTaskMem(pMix);
        }
    }

    private static byte[] Copy(IntPtr format)
    {
        int cbSize = (ushort)Marshal.ReadInt16(format, 16);
        if (cbSize > 22) cbSize = 0; // never read past what a WAVEFORMATEXTENSIBLE can hold; IsValid then refuses it
        var bytes = new byte[18 + cbSize];
        Marshal.Copy(format, bytes, 0, bytes.Length);
        return bytes;
    }
}
