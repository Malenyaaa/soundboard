using System.Globalization;
using System.Text;

namespace Soundboard.Core.Audio;

/// <summary>German display texts for the audio state (used by --check and the device list).</summary>
public static class AudioStateText
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Flow(DeviceFlow f) => f == DeviceFlow.Render ? "Wiedergabe" : "Aufnahme";

    public static string Role(DeviceRoleKind r) => r switch
    {
        DeviceRoleKind.Console => "Konsole",
        DeviceRoleKind.Multimedia => "Multimedia",
        _ => "Kommunikation",
    };

    public static string Status(DeviceStatus s) => s switch
    {
        DeviceStatus.Active => "aktiv",
        DeviceStatus.Disabled => "deaktiviert",
        DeviceStatus.NotPresent => "nicht vorhanden",
        DeviceStatus.Unplugged => "nicht angeschlossen",
        _ => "unbekannt",
    };

    public static string Roles(IReadOnlyList<DeviceRoleKind> roles) =>
        roles.Count == 0 ? "" : string.Join(", ", roles.Select(Role));

    public static string Volume(AudioDeviceInfo d)
    {
        if (d.VolumeScalar is null) return "";
        var pct = Math.Round(d.VolumeScalar.Value * 100f).ToString("0", De) + " %";
        return d.Muted == true ? pct + ", stumm" : pct;
    }

    public static string Mix(MixFormatInfo? m) =>
        m is null ? "" : $"{m.SampleRate.ToString("N0", De)} Hz, {m.Channels} Kanäle, {m.BitsPerSample} bit{(m.IsFloat ? " Float" : "")}";

    /// <summary>Plain-text report for the console.</summary>
    public static string Format(AudioStateReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Soundboard – Audio-Zustand (nur gelesen, nichts verändert), {report.TakenAt.ToString("dd.MM.yyyy HH:mm:ss", De)}");
        sb.AppendLine();
        sb.AppendLine("Standardgeräte");
        foreach (var d in report.Defaults)
        {
            var label = $"{Flow(d.Flow)} / {Role(d.Role)}";
            sb.Append("  ").Append(label.PadRight(28));
            if (d.Id is null) sb.AppendLine("(keins)");
            else sb.AppendLine($"{d.Name}  [{d.Id}]");
        }

        foreach (var flow in new[] { DeviceFlow.Render, DeviceFlow.Capture })
        {
            var list = report.Devices.Where(d => d.Flow == flow).ToList();
            sb.AppendLine();
            sb.AppendLine($"{(flow == DeviceFlow.Render ? "Wiedergabegeräte" : "Aufnahmegeräte")} ({list.Count})");
            if (list.Count == 0) sb.AppendLine("  (keine)");
            foreach (var d in list)
            {
                sb.Append("  ").Append(d.Name).Append("  (").Append(Status(d.Status)).Append(')');
                if (d.DefaultRoles.Count > 0) sb.Append("  Standard: ").Append(Roles(d.DefaultRoles));
                sb.AppendLine();
                sb.AppendLine($"      ID: {d.Id}");
                if (d.Status == DeviceStatus.Active)
                {
                    var vol = d.VolumeScalar is null ? "nicht lesbar" : Volume(d);
                    var mix = d.MixFormat is null ? "nicht lesbar" : Mix(d.MixFormat);
                    sb.AppendLine($"      Lautstärke: {vol} · Mix-Format: {mix}");
                }
            }
        }
        return sb.ToString();
    }
}
