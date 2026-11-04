using Soundboard.App.Hotkeys;
using Soundboard.Core.Audio;
using Soundboard.Core.Config;

namespace Soundboard.App;

/// <summary>
/// Everything the window needs from the outside world. The real set talks to Windows; a
/// preview set (no devices opened, no hotkeys registered) is used to render screenshots.
/// </summary>
public sealed class AppServices
{
    public required AppPaths Paths { get; init; }
    public required IAudioSystem Audio { get; init; }
    public required IHotkeyHost Hotkeys { get; init; }

    public static AppServices CreateReal() => new()
    {
        Paths = AppPaths.Default,
        Audio = new WasapiAudioSystem(),
        Hotkeys = new Win32HotkeyApi(),
    };
}
