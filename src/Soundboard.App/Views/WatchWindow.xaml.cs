using System.Diagnostics;
using System.Windows;
using Soundboard.Core.SystemState;

namespace Soundboard.App.Views;

/// <summary>
/// Small window of the watcher process after the app ended unexpectedly: what that means
/// for Discord, how the Windows defaults look, restart or put the defaults back.
/// </summary>
public partial class WatchWindow : Window
{
    private readonly Func<RestoreResult?> _restore;

    public WatchWindow(CrashReport report, string discordWayBack, Func<RestoreResult?> restore)
    {
        InitializeComponent();
        _restore = restore;
        Headline.Text = report.Headline;
        Body.Text = string.Join(Environment.NewLine, report.Lines(discordWayBack));
        RestoreButton.Visibility = report.DefaultsChanged ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRestart(object sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath;
        if (exe is not null)
        {
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                ShowResult($"Das Soundboard ließ sich nicht starten ({ex.Message}). Starte es bitte von Hand.");
                return;
            }
        }
        Close();
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        var result = _restore();
        if (result is null)
        {
            ShowResult("Es gibt keine Sicherung, auf die sich zurückstellen ließe.");
            return;
        }
        var text = result.RestoredCount == 1
            ? "1 Standardgerät zurückgestellt."
            : $"{result.RestoredCount} Standardgeräte zurückgestellt.";
        if (!result.AllGood) text += " Nicht alles ging automatisch. Von Hand:" + Environment.NewLine + result.ManualSteps();
        ShowResult(text);
        RestoreButton.Visibility = Visibility.Collapsed;
    }

    private void ShowResult(string text)
    {
        Result.Text = text;
        Result.Visibility = Visibility.Visible;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
