namespace Soundboard.Core.Audio;

/// <summary>The virtual cable as found in Windows (only read, matched by endpoint name).</summary>
public sealed record CableInfo(AudioDeviceInfo? Input, AudioDeviceInfo? Output)
{
    /// <summary>Both ends are there and active.</summary>
    public bool IsReady => Input?.Status == DeviceStatus.Active && Output?.Status == DeviceStatus.Active;

    /// <summary>At least one end is known to Windows (maybe disabled or not present).</summary>
    public bool IsKnown => Input is not null || Output is not null;
}

/// <summary>
/// Finds VB-CABLE: the playback endpoint "CABLE Input" (the app writes there) and the
/// recording endpoint "CABLE Output" (Discord reads there). Names as the VB-CABLE driver
/// registers them, e.g. "CABLE Input (VB-Audio Virtual Cable)". The A+B / C+D variants
/// ("CABLE-A Input" …) are not matched.
/// </summary>
public static class CableDetector
{
    public const string InputName = "CABLE Input";
    public const string OutputName = "CABLE Output";

    /// <summary>True for any VB-CABLE endpoint name (used to keep it out of the microphone list).</summary>
    public static bool IsCableName(string? name) =>
        name is not null && (name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)
                             || name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase)
                             || name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase));

    public static CableInfo Detect(AudioStateReport state) =>
        new(Find(state, DeviceFlow.Render, InputName), Find(state, DeviceFlow.Capture, OutputName));

    private static AudioDeviceInfo? Find(AudioStateReport state, DeviceFlow flow, string name) =>
        state.Devices
            .Where(d => d.Flow == flow && d.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Status == DeviceStatus.Active ? 0 : 1)
            .FirstOrDefault();
}
