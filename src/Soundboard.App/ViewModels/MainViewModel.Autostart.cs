using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>
/// "Start with Windows (only in the notification area)" in the appearance tab. The switch shows
/// the real Run entry, also one the installer set; an entry for another copy counts as on and
/// says which file it starts. When Windows refuses the change, the switch springs back and a
/// notice field gives the reason.
/// </summary>
public sealed partial class MainViewModel
{
    private bool _autostartSyncing;
    private string? _autostartOtherPath;
    private string? _autostartErrorReason;
    private bool _autostartErrorWhileOn;

    /// <summary>The Run entry exists (for this or another copy).</summary>
    [ObservableProperty]
    private bool _autostartOn;

    /// <summary>"Starts another copy: …" – empty when the entry starts this copy or there is none.</summary>
    [ObservableProperty]
    private string _autostartOtherText = "";

    /// <summary>Why Windows refused the last change; empty if it did not.</summary>
    [ObservableProperty]
    private string _autostartError = "";

    /// <summary>Reads the entry again (start, opening the settings, after a change).</summary>
    public void RefreshAutostart()
    {
        var state = _services.Autostart.Read();
        _autostartSyncing = true;
        try { AutostartOn = state.Kind != AutostartKind.Off; }
        finally { _autostartSyncing = false; }
        _autostartOtherPath = state.Kind == AutostartKind.OtherCopy ? state.OtherPath : null;
        UpdateAutostartTexts();
    }

    private void UpdateAutostartTexts()
    {
        AutostartOtherText = _autostartOtherPath is null ? "" : L.F("Autostart.OtherCopy", _autostartOtherPath);
        AutostartError = _autostartErrorReason is null ? ""
            : L.F(_autostartErrorWhileOn ? "Autostart.CannotOn" : "Autostart.CannotOff", _autostartErrorReason);
    }

    partial void OnAutostartOnChanged(bool value)
    {
        if (_autostartSyncing) return;
        ApplyAutostart(value, value ? _services.Autostart.Enable() : _services.Autostart.Disable());
    }

    /// <summary>The entry starts another copy: point it at this one.</summary>
    [RelayCommand]
    private void UseThisCopyForAutostart() => ApplyAutostart(true, _services.Autostart.Enable());

    private void ApplyAutostart(bool on, string? error)
    {
        _autostartErrorReason = error;
        _autostartErrorWhileOn = on;
        // After the binding has finished writing: show what is really there (springs back on an error).
        _dispatcher.BeginInvoke(RefreshAutostart);
    }
}
