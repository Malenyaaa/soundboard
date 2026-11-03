using Soundboard.Core.Audio;

namespace Soundboard.App.ViewModels;

/// <summary>One row of the read-only device list.</summary>
public sealed record DeviceRowViewModel(
    string Richtung,
    string Name,
    string Status,
    string Standard,
    string Lautstaerke,
    string Format,
    string Id)
{
    public static DeviceRowViewModel From(AudioDeviceInfo d) => new(
        AudioStateText.Flow(d.Flow),
        d.Name,
        AudioStateText.Status(d.Status),
        AudioStateText.Roles(d.DefaultRoles),
        AudioStateText.Volume(d),
        AudioStateText.Mix(d.MixFormat),
        d.Id);
}

/// <summary>An entry of the monitor device selection, identified by endpoint ID.</summary>
public sealed record OutputChoice(string Id, string Name, bool IsAvailable, bool IsDefault)
{
    public string Display =>
        !IsAvailable ? $"{Name} (nicht angeschlossen)"
        : IsDefault ? $"{Name} (Windows-Standard)"
        : Name;
}
