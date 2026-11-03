namespace Soundboard.Core.Audio;

/// <summary>Direction of an audio endpoint.</summary>
public enum DeviceFlow
{
    Render,
    Capture,
}

/// <summary>The three Windows default-device roles (eConsole, eMultimedia, eCommunications).</summary>
public enum DeviceRoleKind
{
    Console,
    Multimedia,
    Communications,
}

/// <summary>Endpoint state as Windows reports it.</summary>
public enum DeviceStatus
{
    Active,
    Disabled,
    NotPresent,
    Unplugged,
    Unknown,
}

/// <summary>Shared-mode mix format of an endpoint (read only).</summary>
public sealed record MixFormatInfo(int SampleRate, int Channels, int BitsPerSample, bool IsFloat);

/// <summary>One audio endpoint, as read from Windows. Nothing here is ever written back.</summary>
public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    DeviceFlow Flow,
    DeviceStatus Status,
    IReadOnlyList<DeviceRoleKind> DefaultRoles,
    float? VolumeScalar,
    bool? Muted,
    MixFormatInfo? MixFormat);

/// <summary>Default endpoint for one direction and role. Id is null when Windows has none.</summary>
public sealed record DefaultEndpoint(DeviceFlow Flow, DeviceRoleKind Role, string? Id, string? Name);

/// <summary>Complete read-only picture of the audio state at one moment.</summary>
public sealed record AudioStateReport(
    DateTime TakenAt,
    IReadOnlyList<DefaultEndpoint> Defaults,
    IReadOnlyList<AudioDeviceInfo> Devices)
{
    public DefaultEndpoint? GetDefault(DeviceFlow flow, DeviceRoleKind role) =>
        Defaults.FirstOrDefault(d => d.Flow == flow && d.Role == role);

    public AudioDeviceInfo? FindById(string? id) =>
        id is null ? null : Devices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}
