using System.Windows;

namespace Soundboard.App.Views;

/// <summary>
/// Question before an action that cannot be taken back (quit, delete data), in the app's own look
/// instead of a Windows message box. The buttons are verbs ("Quit" / "Keep running"); Esc and the
/// window's close button choose the second one.
/// </summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string heading, string body, string primary, string secondary)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = heading;
        BodyText.Text = body;
        BodyText.Visibility = string.IsNullOrWhiteSpace(body) ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = primary;
        SecondaryButton.Content = secondary;
        Text.UiLanguage.Attach(this);
    }

    /// <summary>Shows the question; true only for the first button.</summary>
    public static bool Ask(Window? owner, string title, string heading, string body, string primary, string secondary)
    {
        var w = new ConfirmWindow(title, heading, body, primary, secondary);
        if (owner is { IsVisible: true }) w.Owner = owner;
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.ShowInTaskbar = true;
        }
        return w.ShowDialog() == true;
    }

    private void OnPrimary(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnSecondary(object sender, RoutedEventArgs e) => DialogResult = false;
}
