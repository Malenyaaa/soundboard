using System.Globalization;
using System.Text;
using Soundboard.Core.Audio;

namespace Soundboard.Core.SystemState;

public enum ComparisonKind
{
    Same,
    Changed,
    Added,
    Missing,
}

/// <summary>One line of "saved vs. now".</summary>
public sealed record ComparisonRow(string What, string Saved, string Now, ComparisonKind Kind, bool IsDefault)
{
    public string KindText => Kind switch
    {
        ComparisonKind.Same => Text.L.Get("Kind.Same"),
        ComparisonKind.Changed => Text.L.Get("Kind.Changed"),
        ComparisonKind.Added => Text.L.Get("Kind.Added"),
        _ => Text.L.Get("Kind.Missing"),
    };
}

/// <summary>Result of comparing a snapshot with the current state.</summary>
public sealed class SnapshotComparison
{
    public required AudioSnapshot Snapshot { get; init; }
    public required IReadOnlyList<ComparisonRow> Rows { get; init; }

    /// <summary>Endpoints present and unchanged in both (not listed one by one).</summary>
    public int UnchangedDevices { get; init; }

    public bool DefaultsChanged => Rows.Any(r => r.IsDefault && r.Kind != ComparisonKind.Same);

    public int DifferenceCount => Rows.Count(r => r.Kind != ComparisonKind.Same);

    public bool IsIdentical => DifferenceCount == 0;

    /// <summary>A virtual cable became a Windows default device since the snapshot.</summary>
    public bool CableBecameDefault { get; init; }

    /// <summary>Defaults that differ, as text lines ("Playback (multimedia): was “A”, now “B”").</summary>
    public IEnumerable<string> DefaultChangeLines =>
        Rows.Where(r => r.IsDefault && r.Kind != ComparisonKind.Same).Select(r => Text.L.F("Snapshot.ChangeLine", r.What, r.Saved, r.Now));
}

/// <summary>What happened to one default role during a restore.</summary>
public enum RestoreOutcome
{
    AlreadySame,
    Restored,

    /// <summary>The saved device is gone or not active; nothing set.</summary>
    SkippedNotAvailable,

    /// <summary>The snapshot had no default for this role; nothing set.</summary>
    SkippedNoSavedDefault,

    /// <summary>The saved device is a virtual cable; the app never makes it a default.</summary>
    SkippedCable,

    Failed,
}

public sealed record RestoreStep(DeviceFlow Flow, DeviceRoleKind Role, string? TargetName, RestoreOutcome Outcome, string? Error);

public sealed class RestoreResult
{
    public required IReadOnlyList<RestoreStep> Steps { get; init; }

    public int RestoredCount => Steps.Count(s => s.Outcome == RestoreOutcome.Restored);

    public bool AllGood => Steps.All(s => s.Outcome is RestoreOutcome.AlreadySame or RestoreOutcome.Restored or RestoreOutcome.SkippedNoSavedDefault);

    /// <summary>Instructions for everything that could not be put back automatically.</summary>
    public string ManualSteps()
    {
        var sb = new StringBuilder();
        foreach (var s in Steps.Where(s => s.Outcome is RestoreOutcome.Failed or RestoreOutcome.SkippedNotAvailable or RestoreOutcome.SkippedCable))
        {
            string where = Text.L.Get(s.Flow == DeviceFlow.Render ? "Restore.WhereRender" : "Restore.WhereCapture");
            string tab = AudioStateText.Flow(s.Flow);
            string how = s.Role == DeviceRoleKind.Communications
                ? Text.L.F("Restore.HowComm", tab, s.TargetName)
                : Text.L.F("Restore.HowDefault", where, s.TargetName);
            string why = s.Outcome switch
            {
                RestoreOutcome.SkippedNotAvailable => Text.L.Get("Restore.WhyNotAvailable"),
                RestoreOutcome.SkippedCable => Text.L.Get("Restore.WhyCable"),
                _ => s.Error is null ? "" : Text.L.F("Restore.WhyError", s.Error),
            };
            sb.AppendLine($"{SnapshotService.RoleText(s.Flow, s.Role)}: {how}{why}");
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Writes a default endpoint. Implemented over IPolicyConfig; replaceable in tests.</summary>
public interface IDefaultEndpointWriter
{
    /// <summary>Makes <paramref name="deviceId"/> the default for <paramref name="role"/>. Throws on failure.</summary>
    void SetDefaultEndpoint(string deviceId, DeviceRoleKind role);
}

/// <summary>Takes, compares and restores audio snapshots.</summary>
public static class SnapshotService
{
    public static string RoleText(DeviceFlow flow, DeviceRoleKind role) =>
        $"{AudioStateText.Flow(flow)} ({AudioStateText.Role(role)})";

    /// <summary>
    /// True when every default that differs from the snapshot is a role the snapshot left empty
    /// (e.g. because the cable was the default playback device when it was taken). Taking the
    /// current state over then replaces no device the user had.
    /// </summary>
    public static bool DefaultsOnlyFillGaps(AudioSnapshot snap, AudioStateReport now)
    {
        foreach (var flow in new[] { DeviceFlow.Render, DeviceFlow.Capture })
        {
            foreach (var role in new[] { DeviceRoleKind.Console, DeviceRoleKind.Multimedia, DeviceRoleKind.Communications })
            {
                var saved = snap.GetDefault(flow, role);
                var cur = now.GetDefault(flow, role);
                if (string.Equals(saved?.Id, cur?.Id, StringComparison.OrdinalIgnoreCase)) continue;
                if (saved?.Id is not null) return false;
            }
        }
        return true;
    }

    public static SnapshotComparison Compare(AudioSnapshot snap, AudioStateReport now)
    {
        var rows = new List<ComparisonRow>();
        bool cableDefault = false;

        foreach (var flow in new[] { DeviceFlow.Render, DeviceFlow.Capture })
        {
            foreach (var role in new[] { DeviceRoleKind.Console, DeviceRoleKind.Multimedia, DeviceRoleKind.Communications })
            {
                var saved = snap.GetDefault(flow, role);
                var cur = now.GetDefault(flow, role);
                bool same = string.Equals(saved?.Id, cur?.Id, StringComparison.OrdinalIgnoreCase);
                rows.Add(new ComparisonRow(
                    Text.L.F("Compare.Default", RoleText(flow, role)),
                    saved?.Name ?? Text.L.Get("Compare.None"),
                    cur?.Name ?? Text.L.Get("Compare.None"),
                    same ? ComparisonKind.Same : ComparisonKind.Changed,
                    true));
                if (!same && CableDetector.IsCableName(cur?.Name) && !CableDetector.IsCableName(saved?.Name)) cableDefault = true;
            }
        }

        int unchanged = 0;
        foreach (var s in snap.Devices)
        {
            var c = now.FindById(s.Id);
            string label = $"{AudioStateText.Flow(s.Flow)}: {s.Name}";
            if (c is null || (c.Status == DeviceStatus.NotPresent && s.Status != DeviceStatus.NotPresent))
            {
                if (s.Status == DeviceStatus.NotPresent) { unchanged++; continue; }
                rows.Add(new ComparisonRow(label, AudioStateText.Status(s.Status), Text.L.Get("Compare.Gone"), ComparisonKind.Missing, false));
                continue;
            }

            bool any = false;
            if (c.Status != s.Status)
            {
                rows.Add(new ComparisonRow(Text.L.F("Compare.State", label), AudioStateText.Status(s.Status), AudioStateText.Status(c.Status), ComparisonKind.Changed, false));
                any = true;
            }
            if (s.Volume is { } sv && c.VolumeScalar is { } cv && Math.Abs(sv - cv) > 0.01f)
            {
                rows.Add(new ComparisonRow(Text.L.F("Compare.Volume", label), Percent(sv), Percent(cv), ComparisonKind.Changed, false));
                any = true;
            }
            if (s.Muted is { } sm && c.Muted is { } cm && sm != cm)
            {
                rows.Add(new ComparisonRow(Text.L.F("Compare.Muted", label), YesNo(sm), YesNo(cm), ComparisonKind.Changed, false));
                any = true;
            }
            if (!any) unchanged++;
        }

        foreach (var c in now.Devices)
        {
            if (c.Status == DeviceStatus.NotPresent) continue;
            if (snap.Devices.Any(s => string.Equals(s.Id, c.Id, StringComparison.OrdinalIgnoreCase))) continue;
            rows.Add(new ComparisonRow($"{AudioStateText.Flow(c.Flow)}: {c.Name}", "–", AudioStateText.Status(c.Status), ComparisonKind.Added, false));
        }

        return new SnapshotComparison { Snapshot = snap, Rows = rows, UnchangedDevices = unchanged, CableBecameDefault = cableDefault };
    }

    /// <summary>
    /// Puts the default devices back to the saved IDs — only those, only where they differ,
    /// only if the saved device is there and active, and never a virtual cable. Volumes,
    /// formats and everything else are not touched.
    /// </summary>
    public static RestoreResult Restore(AudioSnapshot snap, AudioStateReport now, IDefaultEndpointWriter writer)
    {
        var steps = new List<RestoreStep>();
        foreach (var flow in new[] { DeviceFlow.Render, DeviceFlow.Capture })
        {
            foreach (var role in new[] { DeviceRoleKind.Console, DeviceRoleKind.Multimedia, DeviceRoleKind.Communications })
            {
                var saved = snap.GetDefault(flow, role);
                var cur = now.GetDefault(flow, role);
                if (saved?.Id is null)
                {
                    steps.Add(new RestoreStep(flow, role, null, RestoreOutcome.SkippedNoSavedDefault, null));
                    continue;
                }
                if (string.Equals(saved.Id, cur?.Id, StringComparison.OrdinalIgnoreCase))
                {
                    steps.Add(new RestoreStep(flow, role, saved.Name, RestoreOutcome.AlreadySame, null));
                    continue;
                }
                var target = now.FindById(saved.Id);
                if (CableDetector.IsCableName(saved.Name) || CableDetector.IsCableName(target?.Name))
                {
                    steps.Add(new RestoreStep(flow, role, saved.Name, RestoreOutcome.SkippedCable, null));
                    continue;
                }
                if (target is null || target.Status != DeviceStatus.Active || target.Flow != flow)
                {
                    steps.Add(new RestoreStep(flow, role, saved.Name, RestoreOutcome.SkippedNotAvailable, null));
                    continue;
                }
                try
                {
                    writer.SetDefaultEndpoint(target.Id, role);
                    steps.Add(new RestoreStep(flow, role, target.Name, RestoreOutcome.Restored, null));
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException
                                               or UnauthorizedAccessException or InvalidOperationException
                                               or TypeLoadException or NotSupportedException)
                {
                    steps.Add(new RestoreStep(flow, role, target.Name, RestoreOutcome.Failed, ex.Message));
                }
            }
        }
        return new RestoreResult { Steps = steps };
    }

    /// <summary>
    /// Windows (often the VB-CABLE setup) made the cable a default device although a real device
    /// was saved for that role: puts the saved device back, for exactly those roles, and only if
    /// it is there, active and not a cable itself. Roles whose saved default was the cable, roles
    /// that are not the cable now, and roles <paramref name="allowed"/> refuses are left alone.
    /// Steps are listed only for the roles that were looked at.
    /// </summary>
    public static RestoreResult ResetCableDefaults(AudioSnapshot snap, AudioStateReport now, IDefaultEndpointWriter writer,
        Func<DeviceFlow, DeviceRoleKind, bool>? allowed = null)
    {
        var steps = new List<RestoreStep>();
        foreach (var flow in new[] { DeviceFlow.Render, DeviceFlow.Capture })
        {
            foreach (var role in new[] { DeviceRoleKind.Console, DeviceRoleKind.Multimedia, DeviceRoleKind.Communications })
            {
                var cur = now.GetDefault(flow, role);
                // Exactly VB-CABLE's own endpoint in this direction – not Voicemeeter or other VB-Audio devices someone may choose on purpose.
                if (cur?.Id is null || !SystemChanges.IsCableEndpoint(cur.Name ?? now.FindById(cur.Id)?.Name, flow)) continue;
                var saved = snap.GetDefault(flow, role);
                if (saved?.Id is null || CableDetector.IsCableName(saved.Name)) continue;
                if (string.Equals(saved.Id, cur.Id, StringComparison.OrdinalIgnoreCase)) continue;
                if (allowed is not null && !allowed(flow, role)) continue;
                var target = now.FindById(saved.Id);
                if (target is null || target.Status != DeviceStatus.Active || target.Flow != flow || CableDetector.IsCableName(target.Name))
                {
                    steps.Add(new RestoreStep(flow, role, saved.Name, RestoreOutcome.SkippedNotAvailable, null));
                    continue;
                }
                try
                {
                    writer.SetDefaultEndpoint(target.Id, role);
                    steps.Add(new RestoreStep(flow, role, target.Name, RestoreOutcome.Restored, null));
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException
                                               or UnauthorizedAccessException or InvalidOperationException
                                               or TypeLoadException or NotSupportedException)
                {
                    steps.Add(new RestoreStep(flow, role, target.Name, RestoreOutcome.Failed, ex.Message));
                }
            }
        }
        return new RestoreResult { Steps = steps };
    }

    /// <summary>
    /// <paramref name="now"/> with the defaults that <paramref name="result"/> set back, so the
    /// interface need not wait for the next device read to stop warning about the cable.
    /// </summary>
    public static AudioStateReport WithRestoredDefaults(AudioStateReport now, RestoreResult result, AudioSnapshot snap)
    {
        var restored = result.Steps.Where(s => s.Outcome == RestoreOutcome.Restored).ToList();
        if (restored.Count == 0) return now;
        var defaults = now.Defaults.Select(d =>
        {
            if (!restored.Any(s => s.Flow == d.Flow && s.Role == d.Role)) return d;
            var saved = snap.GetDefault(d.Flow, d.Role);
            var dev = now.FindById(saved?.Id);
            return d with { Id = dev?.Id ?? saved?.Id, Name = dev?.Name ?? saved?.Name };
        }).ToList();
        return now with { Defaults = defaults };
    }

    /// <summary>Plain-text report for the console (--restore, --uninstall).</summary>
    public static string Format(SnapshotComparison cmp)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Text.L.F("Report.SavedAt", Text.L.DateTime(cmp.Snapshot.TakenAt)));
        foreach (var r in cmp.Rows)
            sb.AppendLine($"  [{r.KindText,-6}] {r.What}: {(r.Kind == ComparisonKind.Same ? r.Now : $"{r.Saved} → {r.Now}")}");
        if (cmp.UnchangedDevices > 0) sb.AppendLine(Text.L.Count("Report.Unchanged", cmp.UnchangedDevices));
        sb.AppendLine(cmp.IsIdentical ? Text.L.Get("Report.ResultSame") : Text.L.Count("Report.ResultDiff", cmp.DifferenceCount));
        return sb.ToString();
    }

    public static string Format(RestoreResult r)
    {
        var sb = new StringBuilder();
        foreach (var s in r.Steps)
        {
            string text = s.Outcome switch
            {
                RestoreOutcome.AlreadySame => Text.L.Get("Restore.AlreadySame"),
                RestoreOutcome.Restored => Text.L.F("Restore.Restored", s.TargetName),
                RestoreOutcome.SkippedNoSavedDefault => Text.L.Get("Restore.NoSaved"),
                RestoreOutcome.SkippedNotAvailable => Text.L.F("Restore.NotAvailable", s.TargetName),
                RestoreOutcome.SkippedCable => Text.L.Get("Restore.Cable"),
                _ => Text.L.F("Restore.Failed", s.Error),
            };
            sb.AppendLine($"  {RoleText(s.Flow, s.Role)}: {text}");
        }
        var manual = r.ManualSteps();
        if (manual.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Text.L.Get("Report.ByHand"));
            foreach (var line in manual.Split('\n')) sb.AppendLine("  " + line.TrimEnd('\r'));
        }
        return sb.ToString();
    }

    private static string Percent(float v) => Text.L.Percent(v);

    private static string YesNo(bool v) => Text.L.Get(v ? "Compare.Yes" : "Compare.No");
}
