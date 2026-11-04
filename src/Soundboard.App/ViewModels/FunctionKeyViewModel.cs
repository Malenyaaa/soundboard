using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>Something that can wait for a key: a pad or a function key row.</summary>
public interface IKeyTarget
{
    /// <summary>Owner name in the hotkey service.</summary>
    string OwnerId { get; }

    /// <summary>Name shown in the capture hint.</summary>
    string Name { get; }

    bool IsCapturingKey { get; set; }

    string CaptureHint { get; set; }
}

/// <summary>
/// A key that belongs to a function of the program, not to a pad ("Stop all sounds",
/// "Mute microphone"). Shown as a row in the keys tab, built like the pad rows; the properties
/// carry the same names, so both use one row template.
/// </summary>
public sealed partial class FunctionKeyViewModel : ObservableObject, IKeyTarget
{
    private readonly MainViewModel _owner;
    private readonly string _nameKey;
    private Action? _fix;

    public FunctionKeyViewModel(MainViewModel owner, string ownerId, string nameKey)
    {
        _owner = owner;
        OwnerId = ownerId;
        _nameKey = nameKey;
    }

    public string OwnerId { get; }

    public string Name => L.Get(_nameKey);

    /// <summary>The key as text; null without a key.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHotkey))]
    private string? _hotkeyText;

    public bool HasHotkey => HotkeyText is not null;

    [ObservableProperty]
    private bool _hotkeyProblem;

    [ObservableProperty]
    private bool _isCapturingKey;

    [ObservableProperty]
    private string _captureHint = "";

    /// <summary>State in words; for a problem the whole reason.</summary>
    [ObservableProperty]
    private string _hotkeyStateText = "";

    /// <summary>Way out of a problem; null without one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyIssueIsOtherKey))]
    private string? _keyIssueActionText;

    /// <summary>The way out is "Choose another key" – the same as "Change key…", so the list shows only one button.</summary>
    public bool KeyIssueIsOtherKey => KeyIssueActionText == L.Get("Action.OtherKey");

    /// <summary>Sets the problem shown in the row (null clears it).</summary>
    public void SetProblem(string? text, string? actionText, Action? fix)
    {
        HotkeyProblem = text is not null;
        _fix = fix;
        KeyIssueActionText = text is null ? null : actionText;
        if (text is not null) HotkeyStateText = text;
    }

    public void RefreshTexts() => OnPropertyChanged(nameof(Name));

    [RelayCommand]
    private void AssignHotkey() => _owner.BeginKeyCapture(this);

    [RelayCommand]
    private void ClearHotkey() => _owner.SetKey(this, null);

    [RelayCommand]
    private void RunKeyIssueAction() => (_fix ?? (() => _owner.BeginKeyCapture(this)))();
}
