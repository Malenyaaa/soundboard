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
        ComparisonKind.Same => "gleich",
        ComparisonKind.Changed => "anders",
        ComparisonKind.Added => "neu",
        _ => "fehlt",
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

    /// <summary>Defaults that differ, as German text lines ("Wiedergabe (Multimedia): war „A“, jetzt „B“").</summary>
    public IEnumerable<string> DefaultChangeLines =>
        Rows.Where(r => r.IsDefault && r.Kind != ComparisonKind.Same).Select(r => $"{r.What}: war „{r.Saved}“, jetzt „{r.Now}“");
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

    /// <summary>German instructions for everything that could not be put back automatically.</summary>
    public string ManualSteps()
    {
        var sb = new StringBuilder();
        foreach (var s in Steps.Where(s => s.Outcome is RestoreOutcome.Failed or RestoreOutcome.SkippedNotAvailable or RestoreOutcome.SkippedCable))
        {
            string where = s.Flow == DeviceFlow.Render ? "Ausgabe" : "Eingabe";
            string tab = s.Flow == DeviceFlow.Render ? "Wiedergabe" : "Aufnahme";
            string how = s.Role == DeviceRoleKind.Communications
                ? $"Systemsteuerung → Sound → Registerkarte „{tab}“ → Rechtsklick auf „{s.TargetName}“ → „Als Standardkommunikationsgerät festlegen“"
                : $"Windows-Einstellungen → System → Sound → {where}: „{s.TargetName}“ wählen";
            string why = s.Outcome switch
            {
                RestoreOutcome.SkippedNotAvailable => " (das Gerät ist gerade nicht angeschlossen oder deaktiviert)",
                RestoreOutcome.SkippedCable => " (das Soundboard macht ein virtuelles Kabel nie zum Standard)",
                _ => s.Error is null ? "" : $" (Windows meldet: {s.Error})",
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
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string RoleText(DeviceFlow flow, DeviceRoleKind role) =>
        $"{AudioStateText.Flow(flow)} ({AudioStateText.Role(role)})";

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
                    "Standard " + RoleText(flow, role),
                    saved?.Name ?? "keins",
                    cur?.Name ?? "keins",
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
                rows.Add(new ComparisonRow(label, AudioStateText.Status(s.Status), "nicht mehr da", ComparisonKind.Missing, false));
                continue;
            }

            bool any = false;
            if (c.Status != s.Status)
            {
                rows.Add(new ComparisonRow(label + " – Zustand", AudioStateText.Status(s.Status), AudioStateText.Status(c.Status), ComparisonKind.Changed, false));
                any = true;
            }
            if (s.Volume is { } sv && c.VolumeScalar is { } cv && Math.Abs(sv - cv) > 0.01f)
            {
                rows.Add(new ComparisonRow(label + " – Lautstärke", Percent(sv), Percent(cv), ComparisonKind.Changed, false));
                any = true;
            }
            if (s.Muted is { } sm && c.Muted is { } cm && sm != cm)
            {
                rows.Add(new ComparisonRow(label + " – Stumm", sm ? "ja" : "nein", cm ? "ja" : "nein", ComparisonKind.Changed, false));
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

    /// <summary>Plain-text report for the console (--restore, --uninstall).</summary>
    public static string Format(SnapshotComparison cmp)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Gesichert am {cmp.Snapshot.TakenAt.ToString("dd.MM.yyyy HH:mm", De)} – jetzt:");
        foreach (var r in cmp.Rows)
            sb.AppendLine($"  [{r.KindText,-6}] {r.What}: {(r.Kind == ComparisonKind.Same ? r.Now : $"{r.Saved} → {r.Now}")}");
        if (cmp.UnchangedDevices > 0) sb.AppendLine($"  {cmp.UnchangedDevices} weitere Geräte unverändert.");
        sb.AppendLine(cmp.IsIdentical ? "Ergebnis: alles wie gesichert." : $"Ergebnis: {cmp.DifferenceCount} Abweichung(en).");
        return sb.ToString();
    }

    public static string Format(RestoreResult r)
    {
        var sb = new StringBuilder();
        foreach (var s in r.Steps)
        {
            string text = s.Outcome switch
            {
                RestoreOutcome.AlreadySame => "war schon richtig",
                RestoreOutcome.Restored => $"zurückgestellt auf „{s.TargetName}“",
                RestoreOutcome.SkippedNoSavedDefault => "keine Sicherung, nichts geändert",
                RestoreOutcome.SkippedNotAvailable => $"„{s.TargetName}“ ist nicht da, nichts geändert",
                RestoreOutcome.SkippedCable => "gesichert war ein virtuelles Kabel, nichts geändert",
                _ => $"fehlgeschlagen ({s.Error})",
            };
            sb.AppendLine($"  {RoleText(s.Flow, s.Role)}: {text}");
        }
        var manual = r.ManualSteps();
        if (manual.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Von Hand zurückstellen:");
            foreach (var line in manual.Split('\n')) sb.AppendLine("  " + line.TrimEnd('\r'));
        }
        return sb.ToString();
    }

    private static string Percent(float v) => Math.Round(v * 100).ToString("0", De) + " %";
}
