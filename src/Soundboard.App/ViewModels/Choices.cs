using CommunityToolkit.Mvvm.ComponentModel;
using Soundboard.Core.Audio;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>An entry of the monitor device selection, identified by endpoint ID.</summary>
public sealed record OutputChoice(string Id, string Name, bool IsAvailable, bool IsDefault)
{
    public string Display =>
        !IsAvailable ? L.F("Device.NotConnected", Name)
        : IsDefault ? L.F("Device.WindowsDefault", Name)
        : Name;
}

/// <summary>An entry of the buffer size selection; its text follows the interface language.</summary>
public sealed class BufferChoice(BufferSize size) : ObservableObject
{
    public BufferSize Size { get; } = size;

    public string Display => BufferSizes.Display(Size);

    public void RefreshText() => OnPropertyChanged(nameof(Display));
}
