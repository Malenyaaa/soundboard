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
    Uninstall,
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
        ThemeManager.Start(this, Core.Config.ProfileStore.Peek(Core.Config.AppPaths.Default.ProfileFile));
        switch (Mode)
        {
            case StartMode.Watch:
                Watcher.Run(this, WatchPid);
                break;
            case StartMode.Uninstall:
                StartUninstall();
                break;
            default:
                StartMain();
                break;
        }
    }

    private void StartMain()
    {
        try
        {
            _vm = new MainViewModel(AppServices.CreateReal());
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            MessageBox.Show($"Der Windows-Audiodienst antwortet nicht ({ex.Message}). Starte Windows neu oder prüfe in den Diensten, ob „Windows-Audio“ läuft, und öffne das Soundboard dann noch einmal.",
                "Soundboard", MessageBoxButton.OK, MessageBoxImage.Warning);
            Instance?.Dispose();
            Instance = null;
            Shutdown();
            return;
        }
        _window = new Views.MainWindow(_vm);
        MainWindow = _window;
        _tray = new TrayIcon(this, _vm);
        Instance?.ListenForShow(() => Dispatcher.BeginInvoke(ShowMain));
        Watcher.StartFor(Environment.ProcessId);
        if (!StartInTray) _window.Show();
    }

    private void StartUninstall()
    {
        var window = new Views.UninstallWindow(
            Core.Config.AppPaths.Default,
            ReadStateSafe,
            new Core.SystemState.PolicyConfigWriter(),
            SingleInstance.IsRunning);
        window.Closed += (_, _) => Shutdown();
        MainWindow = window;
        window.Show();
    }

    private static Core.Audio.AudioStateReport ReadStateSafe()
    {
        try
        {
            using var catalog = new Core.Audio.DeviceCatalog();
            return catalog.ReadState();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return new Core.Audio.AudioStateReport(DateTime.Now, [], []);
        }
    }

    /// <summary>"Alles entfernen" from the tray: open the removal guide and end this copy.</summary>
    public void RequestUninstall()
    {
        if (_vm is null || _exiting) return;
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        var text = "Die Anleitung zum Entfernen öffnet sich, und das Soundboard wird dafür beendet."
            + (_vm.CableRunning ? " Discord hört danach Stille vom Kabel." : "")
            + Environment.NewLine + Environment.NewLine + "Fortfahren?";
        var owner = _window is { IsVisible: true } ? _window : null;
        var answer = owner is null
            ? MessageBox.Show(text, "Alles entfernen", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(owner, text, "Alles entfernen", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        ExitNow();
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--uninstall") { UseShellExecute = false }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
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
