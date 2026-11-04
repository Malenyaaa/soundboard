using System.Windows;
using System.Windows.Threading;
using Soundboard.App.Themes;
using Soundboard.App.ViewModels;

namespace Soundboard.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUnhandled;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.FollowWindows(this);
        var window = new Views.MainWindow(new MainViewModel(AppServices.CreateReal()));
        window.Closed += (_, _) => Shutdown();
        MainWindow = window;
        window.Show();
    }

    /// <summary>Unexpected errors become a yellow notice instead of closing the program.</summary>
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (MainWindow?.DataContext is MainViewModel vm)
        {
            vm.ShowNotice("unexpected", $"Unerwarteter Fehler: {e.Exception.Message}. Das Programm läuft weiter; wenn etwas nicht mehr geht, bitte neu starten.");
            e.Handled = true;
        }
    }
}
