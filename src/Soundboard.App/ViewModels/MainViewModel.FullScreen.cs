using CommunityToolkit.Mvvm.ComponentModel;
using Soundboard.Core.Hotkeys;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>
/// Full screen (F11): only the pads, large, plus a slim bar with stop, volume and the way back.
/// The window does the switching; this part holds the state and the texts. Full screen is never
/// stored: the next start opens the normal window.
/// </summary>
public sealed partial class MainViewModel
{
    public const int VkF11 = 0x7A;

    /// <summary>Full screen is on: header, notices, settings and status line are hidden.</summary>
    [ObservableProperty]
    private bool _isFullScreen;

    /// <summary>Tooltip of the full screen button; says so when F11 belongs to a pad.</summary>
    [ObservableProperty]
    private string _fullScreenToolTip = "";

    /// <summary>The pad that has F11 as its global key, if any: then F11 plays that pad everywhere, also in this window.</summary>
    public TileViewModel? F11Pad => HotkeysPaused ? null : Tiles.FirstOrDefault(t => HotkeyGesture.Parse(t.Config.Hotkey) is { Modifiers: HotkeyModifiers.None, VirtualKey: VkF11 });

    /// <summary>Before the window goes full screen: nothing open may hide behind it.</summary>
    public void PrepareFullScreen()
    {
        CancelKeyCapture();
        foreach (var t in Tiles) t.IsIssueOpen = false;
        if (SettingsOpen) CloseSettings();
    }

    /// <summary>Also after a pad was renamed (its name is in the tooltip).</summary>
    public void UpdateFullScreenToolTip()
    {
        var pad = F11Pad;
        FullScreenToolTip = pad is null ? L.Get("Main.FullScreenTip") : L.F("Main.FullScreenTipTaken", pad.Name);
    }
}
