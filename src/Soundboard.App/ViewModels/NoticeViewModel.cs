using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Soundboard.App.ViewModels;

/// <summary>A yellow notice: what is missing and what to do. Optional action button.</summary>
public sealed partial class NoticeViewModel : ObservableObject
{
    private readonly Action<NoticeViewModel> _close;
    private readonly Action? _action;

    public NoticeViewModel(string key, string text, Action<NoticeViewModel> close, string? actionText = null, Action? action = null)
    {
        Key = key;
        Text = text;
        _close = close;
        ActionText = actionText;
        _action = action;
    }

    /// <summary>Same key replaces an older notice instead of stacking up.</summary>
    public string Key { get; }

    public string Text { get; }

    public string? ActionText { get; }

    public bool HasAction => _action is not null && !string.IsNullOrEmpty(ActionText);

    [RelayCommand]
    private void Close() => _close(this);

    [RelayCommand]
    private void RunAction()
    {
        _close(this);
        _action?.Invoke();
    }
}
