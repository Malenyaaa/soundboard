using System.Windows;
using System.Windows.Threading;
using Soundboard.App.Themes;
using Soundboard.App.Tray;
using Soundboard.App.ViewModels;

namespace Soundboard.App;

/// <summary>How the process was started (set by <see cref="Program"/> before Run).</summary>
internal enum StartMode
{
    Main,
    Watch,
}

public partial class App : Application
{
    private Views.MainWindow? _window;
    private MainViewModel? _vm;
    private TrayIcon? _tray;
    private bool _exiting;

    public App()
    {
        DispatcherUnhandledException += OnUnhandled;
    }

    internal static StartMode Mode { get; set; } = StartMode.Main;
    internal static int WatchPid { get; set; }
    internal static bool StartInTray { get; set; }
    internal static SingleInstance? Instance { get; set; }

    /// <summary>True once the user confirmed "Beenden"; closing the window then really closes.</summary>
    public bool IsExiting => _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.FollowWindows(this);
        switch (Mode)
        {
            case StartMode.Watch:
                Watcher.Run(this, WatchPid);
                break;
            default:
                StartMain();
                break;
        }
    }

    private void StartMain()
    {
        _vm = new MainViewModel(AppServices.CreateReal());
        _window = new Views.MainWindow(_vm);
        MainWindow = _window;
        _tray = new TrayIcon(this, _vm);
        Instance?.ListenForShow(() => Dispatcher.BeginInvoke(ShowMain));
        Watcher.StartFor(Environment.ProcessId);
        if (!StartInTray) _window.Show();
    }

    /// <summary>Brings the main window back (tray click, second start).</summary>
    public void ShowMain()
    {
        if (_window is null || _exiting) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>The window was closed with X: keep running in the tray, say so once.</summary>
    public void OnHiddenToTray()
    {
        if (_vm is null || _vm.TrayHintShown) return;
        _vm.TrayHintShown = true;
        _tray?.ShowHint("Soundboard läuft weiter",
            "Mikrofon und Sounds gehen weiter ans Kabel. Öffnen und Beenden über das Notensymbol im Infobereich.");
    }

    /// <summary>"Beenden" from the tray: explain what Discord gets and how the audio state looks.</summary>
    public void RequestExit()
    {
        if (_vm is null || _exiting) return;
        var owner = _window is { IsVisible: true } ? _window : null;
        var text = _vm.ExitQuestion();
        var answer = owner is null
            ? MessageBox.Show(text, "Soundboard beenden", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(owner, text, "Soundboard beenden", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) ExitNow();
    }

    /// <summary>Ends without asking (after confirmation or for the uninstall guide).</summary>
    public void ExitNow()
    {
        if (_exiting) return;
        _exiting = true;
        _vm?.Dispose();
        _tray?.Dispose();
        _window?.Close();
        Instance?.Dispose();
        Instance = null;
        Shutdown();
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
