using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Soundboard.App.ViewModels;

/// <summary>
/// A yellow notice: what is missing and what to do. Optional action button. The texts are
/// produced on demand, so a notice follows a change of the interface language.
/// </summary>
public sealed partial class NoticeViewModel : ObservableObject
{
    private readonly Action<NoticeViewModel> _close;
    private readonly Action? _action;
    private readonly Func<string> _text;
    private readonly Func<string>? _actionText;

    public NoticeViewModel(string key, Func<string> text, Action<NoticeViewModel> close, Func<string>? actionText = null, Action? action = null)
    {
        Key = key;
        _text = text;
        _close = close;
        _actionText = actionText;
        _action = action;
    }

    /// <summary>Same key replaces an older notice instead of stacking up.</summary>
    public string Key { get; }

    public string Text => _text();

    public string? ActionText => _actionText?.Invoke();

    public bool HasAction => _action is not null && _actionText is not null;

    /// <summary>The language changed: read the texts again.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(ActionText));
    }

    [RelayCommand]
    private void Close() => _close(this);

    [RelayCommand]
    private void RunAction()
    {
        _close(this);
        _action?.Invoke();
    }
}
