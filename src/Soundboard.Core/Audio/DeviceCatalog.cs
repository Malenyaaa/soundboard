using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Soundboard.Core.Audio;

/// <summary>
/// Lists audio endpoints and opens them by ID. Strictly read-only: never sets default
/// devices, volumes, formats or any other endpoint property.
/// </summary>
public sealed class DeviceCatalog : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private NotificationClient? _notifications;

    /// <summary>
    /// Raised when an endpoint appears, disappears, changes state or a default changes.
    /// Raised on a Windows audio thread — marshal to the UI thread before touching UI.
    /// </summary>
    public event EventHandler? DevicesChanged;

    /// <summary>Starts listening for endpoint changes (IMMNotificationClient).</summary>
    public void StartWatching()
    {
        if (_notifications is not null) return;
        _notifications = new NotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_notifications);
    }

    /// <summary>Reads the current audio state: defaults per role and all endpoints.</summary>
    public AudioStateReport ReadState()
    {
        var defaults = new List<DefaultEndpoint>();
        foreach (var flow in new[] { DeviceFlow.Render, DeviceFlow.Capture })
        {
            foreach (var role in new[] { DeviceRoleKind.Console, DeviceRoleKind.Multimedia, DeviceRoleKind.Communications })
            {
                string? id = null, name = null;
                try
                {
                    if (_enumerator.HasDefaultAudioEndpoint(ToDataFlow(flow), ToRole(role)))
                    {
                        using var dev = _enumerator.GetDefaultAudioEndpoint(ToDataFlow(flow), ToRole(role));
                        id = dev.ID;
                        name = SafeName(dev);
                    }
                }
                catch (COMException)
                {
                    // No default for this role — leave empty.
                }
                defaults.Add(new DefaultEndpoint(flow, role, id, name));
            }
        }

        var devices = new List<AudioDeviceInfo>();
        MMDeviceCollection collection;
        try
        {
            collection = _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.All);
        }
        catch (COMException)
        {
            return new AudioStateReport(DateTime.Now, defaults, devices);
        }

        foreach (var dev in collection)
        {
            using (dev)
            {
                devices.Add(ReadDevice(dev, defaults));
            }
        }

        devices.Sort((a, b) =>
        {
            int c = a.Flow.CompareTo(b.Flow);
            if (c != 0) return c;
            c = StatusOrder(a.Status).CompareTo(StatusOrder(b.Status));
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        return new AudioStateReport(DateTime.Now, defaults, devices);
    }

    /// <summary>
    /// Opens an active endpoint by ID. Returns null if it does not exist or is not active.
    /// The caller owns the returned device.
    /// </summary>
    public MMDevice? OpenActive(string id)
    {
        try
        {
            var dev = _enumerator.GetDevice(id);
            if (dev.State == DeviceState.Active) return dev;
            dev.Dispose();
        }
        catch (COMException)
        {
        }
        catch (ArgumentException)
        {
        }
        return null;
    }

    /// <summary>Reads the shared-mode mix format of an active endpoint.</summary>
    public static MixFormatInfo? ReadMixFormat(MMDevice device)
    {
        try
        {
            using var client = device.AudioClient;
            return ToInfo(client.MixFormat);
        }
        catch (COMException)
        {
            return null;
        }
    }

    internal static MixFormatInfo ToInfo(WaveFormat f)
    {
        bool isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat
            || (f is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        return new MixFormatInfo(f.SampleRate, f.Channels, f.BitsPerSample, isFloat);
    }

    private static AudioDeviceInfo ReadDevice(MMDevice dev, List<DefaultEndpoint> defaults)
    {
        var flow = dev.DataFlow == DataFlow.Capture ? DeviceFlow.Capture : DeviceFlow.Render;
        var status = ToStatus(dev.State);
        var roles = defaults
            .Where(d => d.Flow == flow && d.Id is not null && string.Equals(d.Id, dev.ID, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Role)
            .ToList();

        float? volume = null;
        bool? muted = null;
        MixFormatInfo? mix = null;
        if (status == DeviceStatus.Active)
        {
            try
            {
                // Reading only. AudioEndpointVolume registers a change callback internally,
                // which Dispose removes again; no value is set.
                using var vol = dev.AudioEndpointVolume;
                volume = vol.MasterVolumeLevelScalar;
                muted = vol.Mute;
            }
            catch (COMException)
            {
            }
            mix = ReadMixFormat(dev);
        }

        return new AudioDeviceInfo(dev.ID, SafeName(dev), flow, status, roles, volume, muted, mix);
    }

    private static string SafeName(MMDevice dev)
    {
        try
        {
            var name = dev.FriendlyName;
            return string.IsNullOrWhiteSpace(name) ? Text.L.Get("Device.NoName") : name;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or KeyNotFoundException)
        {
            return Text.L.Get("Device.NameUnreadable");
        }
    }

    private static int StatusOrder(DeviceStatus s) => s switch
    {
        DeviceStatus.Active => 0,
        DeviceStatus.Unplugged => 1,
        DeviceStatus.Disabled => 2,
        DeviceStatus.NotPresent => 3,
        _ => 4,
    };

    private static DeviceStatus ToStatus(DeviceState s) => s switch
    {
        DeviceState.Active => DeviceStatus.Active,
        DeviceState.Disabled => DeviceStatus.Disabled,
        DeviceState.NotPresent => DeviceStatus.NotPresent,
        DeviceState.Unplugged => DeviceStatus.Unplugged,
        _ => DeviceStatus.Unknown,
    };

    internal static DataFlow ToDataFlow(DeviceFlow f) => f == DeviceFlow.Capture ? DataFlow.Capture : DataFlow.Render;

    internal static Role ToRole(DeviceRoleKind r) => r switch
    {
        DeviceRoleKind.Console => Role.Console,
        DeviceRoleKind.Multimedia => Role.Multimedia,
        _ => Role.Communications,
    };

    public void Dispose()
    {
        if (_notifications is not null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(_notifications); }
            catch (COMException) { }
            _notifications = null;
        }
        _enumerator.Dispose();
    }

    private sealed class NotificationClient(DeviceCatalog owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => owner.DevicesChanged?.Invoke(owner, EventArgs.Empty);
        public void OnDeviceAdded(string pwstrDeviceId) => owner.DevicesChanged?.Invoke(owner, EventArgs.Empty);
        public void OnDeviceRemoved(string deviceId) => owner.DevicesChanged?.Invoke(owner, EventArgs.Empty);
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => owner.DevicesChanged?.Invoke(owner, EventArgs.Empty);

        // Property changes fire often (e.g. format or name metadata); not needed for the list.
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
