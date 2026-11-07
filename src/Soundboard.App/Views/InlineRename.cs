using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Soundboard.App.Views;

/// <summary>Something renamed in place: a text box over it while <see cref="IsRenaming"/> is set.</summary>
public interface IInlineRenamable
{
    string Name { get; set; }

    bool IsRenaming { get; set; }
}

/// <summary>
/// Renaming in place (<c>views:InlineRename.IsEnabled="True"</c> on the text box, whose data is an
/// <see cref="IInlineRenamable"/>): the box opens with the name selected; Enter or leaving the box keeps the
/// typed name, Esc keeps the old one; an empty name keeps the old one as well. A click anywhere else in the
/// window counts as leaving, also on parts that take no focus (the window calls <see cref="ConfirmIfOutside"/>).
/// </summary>
public static class InlineRename
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(InlineRename), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || e.NewValue is not true) return;
        box.PreviewKeyDown += (_, k) => k.Handled = HandleKey(box, k.Key);
        box.LostKeyboardFocus += (_, _) => Confirm(box);
        box.IsVisibleChanged += (_, _) => Open(box);
        // A box that is open from the start (a new category) may become visible before its data arrives.
        box.Loaded += (_, _) => Open(box);
    }

    /// <summary>The box just came into view: the current name in it, selected, with the keyboard focus.</summary>
    private static void Open(TextBox box)
    {
        if (!box.IsVisible || box.DataContext is not IInlineRenamable { IsRenaming: true } target) return;
        box.Text = target.Name;
        box.Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    /// <summary>The name that counts: the typed one, trimmed; the old one when nothing is typed.</summary>
    public static string Resolve(string old, string? typed) => string.IsNullOrWhiteSpace(typed) ? old : typed.Trim();

    /// <summary>Keeps the typed name and closes the box (nothing happens when it is not open).</summary>
    public static void Confirm(TextBox box)
    {
        if (box.DataContext is not IInlineRenamable { IsRenaming: true } target) return;
        var name = Resolve(target.Name, box.Text);
        if (name != target.Name) target.Name = name;
        target.IsRenaming = false;
    }

    /// <summary>Closes the box and keeps the old name.</summary>
    public static void Cancel(TextBox box)
    {
        if (box.DataContext is IInlineRenamable { IsRenaming: true } target) target.IsRenaming = false;
    }

    /// <summary>Enter keeps, Esc discards. Returns true when the key was used.</summary>
    public static bool HandleKey(TextBox box, Key key)
    {
        if (key == Key.Enter) Confirm(box);
        else if (key == Key.Escape) Cancel(box);
        else return false;
        return true;
    }

    /// <summary>
    /// A mouse press somewhere in the window: when it is outside the open box, the typed name is kept.
    /// The click itself goes on as usual. Returns true when a name was confirmed.
    /// </summary>
    public static bool ConfirmIfOutside(TextBox? open, DependencyObject? clicked)
    {
        if (open?.DataContext is not IInlineRenamable { IsRenaming: true }) return false;
        for (var d = clicked; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, open)) return false;
        Confirm(open);
        return true;
    }
}
