using Soundboard.Core.Audio;
using Soundboard.Core.Text;

namespace Soundboard.Core.SystemState;

/// <summary>Why a Windows default role gives no sound.</summary>
public enum AudioProblemKind
{
    /// <summary>Windows has no default for the role, though one was saved.</summary>
    NoDevice,

    /// <summary>The default device is disabled, unplugged or not present.</summary>
    NotActive,

    /// <summary>The virtual cable is the default: playback goes into the cable, or the default microphone is the cable.</summary>
    CableDefault,

    /// <summary>The default playback device is muted.</summary>
    Muted,

    /// <summary>The default playback device is at 0 %.</summary>
    Silent,
}

/// <summary>One thing that leaves a Windows default role without sound, for one or more roles of the same direction.</summary>
public sealed record AudioProblem(DeviceFlow Flow, IReadOnlyList<DeviceRoleKind> Roles, AudioProblemKind Kind, string? DeviceName, DeviceStatus Status)
{
    public string Text
    {
        get
        {
            string where = $"{AudioStateText.Flow(Flow)} ({string.Join(", ", Roles.Select(AudioStateText.Role))})";
            string name = DeviceName ?? "?";
            return Kind switch
            {
                AudioProblemKind.NoDevice => L.F("Health.NoDevice", where, name),
                AudioProblemKind.NotActive => L.F("Health.NotActive", where, name, AudioStateText.Status(Status)),
                AudioProblemKind.CableDefault => L.F(Flow == DeviceFlow.Render ? "Health.CableRender" : "Health.CableCapture", where, name),
                AudioProblemKind.Muted => L.F("Health.Muted", where, name),
                _ => L.F("Health.Silent", where, name),
            };
        }
    }
}

/// <summary>
/// Decides whether Windows audio still works — not whether it is the same as saved. A default
/// device that differs from the saved one but works (headphones instead of speakers, chosen by
/// the user) is no problem. Only what leaves a default role without sound counts. Unclear cases
/// count as fine. Reads only; nothing here changes Windows.
/// </summary>
public static class AudioHealth
{
    /// <summary>Below this the default playback device counts as silent (0 %).</summary>
    public const float SilentBelow = 0.005f;

    private static readonly DeviceRoleKind[] CheckedRoles = [DeviceRoleKind.Multimedia, DeviceRoleKind.Communications];

    /// <summary>
    /// The problems of the current state, roles of one direction with the same cause merged.
    /// <paramref name="saved"/> may be null (nothing saved): then a missing default is not
    /// reported, because it may never have been there.
    /// </summary>
    public static IReadOnlyList<AudioProblem> Check(AudioStateReport now, AudioSnapshot? saved)
    {
        var found = new List<(DeviceFlow Flow, DeviceRoleKind Role, AudioProblemKind Kind, string? Name, DeviceStatus Status)>();
        foreach (var flow in new[] { DeviceFlow.Render, DeviceFlow.Capture })
        {
            foreach (var role in CheckedRoles)
            {
                var was = saved?.GetDefault(flow, role);
                var cur = now.GetDefault(flow, role);
                if (cur?.Id is null)
                {
                    // Nothing there now. A problem only if something was there – and it was not the cable.
                    if (was?.Id is not null && !CableDetector.IsCableName(was.Name))
                        found.Add((flow, role, AudioProblemKind.NoDevice, was.Name, DeviceStatus.Unknown));
                    continue;
                }

                var dev = now.FindById(cur.Id);
                string? name = cur.Name ?? dev?.Name;
                if (dev is not null && dev.Status != DeviceStatus.Active)
                {
                    found.Add((flow, role, AudioProblemKind.NotActive, name, dev.Status));
                    continue;
                }

                if (CableDetector.IsCableName(name))
                {
                    // Playback into the cable is never heard. The cable as default microphone is a
                    // problem only when it became so – someone may have chosen it on purpose.
                    bool chosenBefore = string.Equals(was?.Id, cur.Id, StringComparison.OrdinalIgnoreCase);
                    if (flow == DeviceFlow.Render || !chosenBefore)
                        found.Add((flow, role, AudioProblemKind.CableDefault, name, DeviceStatus.Active));
                    continue;
                }

                if (flow == DeviceFlow.Render && dev is not null)
                {
                    if (dev.Muted == true) found.Add((flow, role, AudioProblemKind.Muted, name, DeviceStatus.Active));
                    else if (dev.VolumeScalar is { } v && v < SilentBelow) found.Add((flow, role, AudioProblemKind.Silent, name, DeviceStatus.Active));
                }
            }
        }

        return [.. found
            .GroupBy(f => (f.Flow, f.Kind, Name: f.Name ?? ""))
            .Select(g => new AudioProblem(g.Key.Flow, [.. g.Select(f => f.Role)], g.Key.Kind, g.First().Name, g.First().Status))];
    }
}
