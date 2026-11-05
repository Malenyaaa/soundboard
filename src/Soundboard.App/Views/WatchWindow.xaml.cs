using System.Diagnostics;
using System.Windows;
using Soundboard.Core.SystemState;
using Soundboard.Core.Text;

namespace Soundboard.App.Views;

/// <summary>
/// Small window of the watcher process, only after the app ended unexpectedly while the cable
/// ran: Discord's microphone is silent now. Offers to restart the soundboard.
/// </summary>
public partial class WatchWindow : Window
{
    public WatchWindow(CrashReport report, string discordWayBack)
    {
        InitializeComponent();
        Text.UiLanguage.Attach(this);
        Headline.Text = report.Headline;
        Body.Text = string.Join(Environment.NewLine, report.Lines(discordWayBack));
    }

    private void OnRestart(object sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath;
        if (exe is not null)
        {
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Result.Text = L.F("Watch.StartFailed", ex.Message);
                Result.Visibility = Visibility.Visible;
                return;
            }
        }
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
