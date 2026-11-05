using Soundboard.App.Hotkeys;
using Soundboard.Core.Audio;
using Soundboard.Core.Config;
using Soundboard.Core.SystemState;

namespace Soundboard.App;

/// <summary>
/// Everything the window needs from the outside world. The real set talks to Windows; a
/// preview set (no devices opened, no hotkeys registered, nothing written to Windows) is
/// used to render screenshots.
/// </summary>
public sealed class AppServices
{
    public required AppPaths Paths { get; init; }
    public required IAudioSystem Audio { get; init; }
    public required IHotkeyHost Hotkeys { get; init; }

    /// <summary>Only used when the user asks to put the saved default devices back.</summary>
    public required IDefaultEndpointWriter Policy { get; init; }

    /// <summary>Per-user registry for the settings the app changes and puts back (a fake for previews and tests).</summary>
    public required IUserRegistry Registry { get; init; }

    /// <summary>Endpoint formats (only ever written for the virtual cable); a fake for previews and tests.</summary>
    public required IDeviceFormatApi Formats { get; init; }

    /// <summary>"Start with Windows": the Run entry of this program file (a fake for previews and tests).</summary>
    public required Autostart Autostart { get; init; }

    /// <summary>Write the running flag (off for previews).</summary>
    public bool UseDirtyFlag { get; init; } = true;

    public static AppServices CreateReal() => new()
    {
        Paths = AppPaths.Default,
        Audio = new WasapiAudioSystem(),
        Hotkeys = new Win32HotkeyApi(),
        Policy = new PolicyConfigWriter(),
        Registry = new UserRegistry(),
        Formats = new PolicyConfigFormatApi(),
        Autostart = new Autostart(new RegistryRunKey(), Environment.ProcessPath),
    };
}
