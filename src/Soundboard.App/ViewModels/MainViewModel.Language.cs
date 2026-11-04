using CommunityToolkit.Mvvm.ComponentModel;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>Language of the interface: choice in the settings, and every computed text follows a change at once.</summary>
public sealed partial class MainViewModel
{
    /// <summary>A text that is looked up when shown (notices, problems on pads).</summary>
    private static Func<string> T(string key) => () => L.Get(key);

    /// <summary>A text with placeholders that is looked up when shown.</summary>
    private static Func<string> TF(string key, params object?[] args) => () => L.F(key, args);

    /// <summary>"en" or "de"; switching applies at once and is stored in the profile.</summary>
    public string Language
    {
        get => _profile.Language;
        set
        {
            var lang = L.Normalize(value);
            if (lang == _profile.Language) return;
            _profile.Language = lang;
            ScheduleSave();
            OnPropertyChanged();
            L.SetLanguage(lang);
        }
    }

    /// <summary>"Also show devices that are no longer there (3)".</summary>
    [ObservableProperty]
    private string _notPresentText = "";

    /// <summary>Tooltip of the cable part of the status line.</summary>
    [ObservableProperty]
    private string _statusCableTip = "";

    /// <summary>Tab the cable part of the status line opens: the setup while the cable is missing.</summary>
    [ObservableProperty]
    private string _statusCableTab = SettingsTabs.MicAndCable;

    /// <summary>"“Stop”: Pause. Num keys give “Num 1” … only with Num Lock on." in the keys tab.</summary>
    [ObservableProperty]
    private string _stopKeyLine = "";

    public string MasterVolumeText => L.Percent(MasterVolumePercent / 100.0);

    public string MicVolumeText => L.Percent(MicVolumePercent / 100.0);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        CancelKeyCapture();
        RefreshDeviceViews();
        UpdateMonitorStatus();
        // The comparison texts are rebuilt in the new language; a notice the user already closed stays closed.
        bool hadSnapshotNotice = Notices.Any(n => n.Key == "snapshot");
        CompareNow();
        if (!hadSnapshotNotice) ClearNotice("snapshot");
        UpdateCableStatus();
        RefreshHotkeyViews();
        foreach (var b in BufferChoices) b.RefreshText();
        foreach (var t in Tiles)
        {
            t.RefreshTexts();
            if (t.State == TileState.Error) _ = LoadTileAsync(t); // the reason comes from the loader: ask again
        }
        foreach (var n in Notices) n.RefreshTexts();
        UpdateNoticeView();
        RestoreResultText = "";
        if (_mixPurpose != MixPurpose.FileTest) TestStatus = "";
        StatusMix = "";
        UpdateMixStatus();
        foreach (var a in AccentChoices) a.RefreshText();
        UpdateCustomAccentHint();
        OnPropertyChanged(nameof(StopAllToolTip));
        OnPropertyChanged(nameof(MasterVolumeText));
        OnPropertyChanged(nameof(MicVolumeText));
        OnPropertyChanged(nameof(Language));
    }
}
