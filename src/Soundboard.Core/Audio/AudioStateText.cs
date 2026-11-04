using System.Text;
using Soundboard.Core.Text;

namespace Soundboard.Core.Audio;

/// <summary>Display texts for the audio state (used by --check and the device list), in the language of the interface.</summary>
public static class AudioStateText
{
    public static string Flow(DeviceFlow f) => L.Get(f == DeviceFlow.Render ? "Flow.Render" : "Flow.Capture");

    public static string Role(DeviceRoleKind r) => L.Get(r switch
    {
        DeviceRoleKind.Console => "Role.Console",
        DeviceRoleKind.Multimedia => "Role.Multimedia",
        _ => "Role.Communications",
    });

    public static string Status(DeviceStatus s) => L.Get(s switch
    {
        DeviceStatus.Active => "Status.Active",
        DeviceStatus.Disabled => "Status.Disabled",
        DeviceStatus.NotPresent => "Status.NotPresent",
        DeviceStatus.Unplugged => "Status.Unplugged",
        _ => "Status.Unknown",
    });

    public static string Roles(IReadOnlyList<DeviceRoleKind> roles) =>
        roles.Count == 0 ? "" : string.Join(", ", roles.Select(Role));

    public static string Volume(AudioDeviceInfo d)
    {
        if (d.VolumeScalar is null) return "";
        var pct = L.Percent(d.VolumeScalar.Value);
        return d.Muted == true ? L.F("Volume.Muted", pct) : pct;
    }

    public static string Mix(MixFormatInfo? m) =>
        m is null ? "" : L.F(m.IsFloat ? "Mix.Float" : "Mix.Int", m.SampleRate, m.Channels, m.BitsPerSample);

    /// <summary>Plain-text report for the console.</summary>
    public static string Format(AudioStateReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine(L.F("Check.Title", L.DateTimeSeconds(report.TakenAt)));
        sb.AppendLine();
        sb.AppendLine(L.Get("Check.Defaults"));
        foreach (var d in report.Defaults)
        {
            var label = $"{Flow(d.Flow)} / {Role(d.Role)}";
            sb.Append("  ").Append(label.PadRight(28));
            if (d.Id is null) sb.AppendLine(L.Get("Check.NoDefault"));
            else sb.AppendLine($"{d.Name}  [{d.Id}]");
        }

        foreach (var flow in new[] { DeviceFlow.Render, DeviceFlow.Capture })
        {
            var list = report.Devices.Where(d => d.Flow == flow).ToList();
            sb.AppendLine();
            sb.AppendLine(L.F(flow == DeviceFlow.Render ? "Check.RenderDevices" : "Check.CaptureDevices", list.Count));
            if (list.Count == 0) sb.AppendLine("  " + L.Get("Check.NoDevices"));
            foreach (var d in list)
            {
                sb.Append("  ").Append(d.Name).Append("  (").Append(Status(d.Status)).Append(')');
                if (d.DefaultRoles.Count > 0) sb.Append(L.F("Check.DefaultFor", Roles(d.DefaultRoles)));
                sb.AppendLine();
                sb.AppendLine($"      ID: {d.Id}");
                if (d.Status == DeviceStatus.Active)
                {
                    var vol = d.VolumeScalar is null ? L.Get("Check.Unreadable") : Volume(d);
                    var mix = d.MixFormat is null ? L.Get("Check.Unreadable") : Mix(d.MixFormat);
                    sb.AppendLine(L.F("Check.VolumeLine", vol, mix));
                }
            }
        }
        return sb.ToString();
    }
}
