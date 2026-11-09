using System.Windows;
using System.Windows.Threading;
using Soundboard.App.Themes;
using Soundboard.App.Tray;
using Soundboard.App.ViewModels;
using Soundboard.Core.Text;

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

    /// <summary>Start time of the watched process (local ticks), so a reused process ID is not mistaken for it; 0 when not given.</summary>
    internal static long WatchStartTicks { get; set; }
    internal static bool StartInTray { get; set; }
    internal static SingleInstance? Instance { get; set; }

    /// <summary>True once the user confirmed "Quit"; closing the window then really closes.</summary>
    public bool IsExiting => _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var profile = Core.Config.ProfileStore.Peek(Core.Config.AppPaths.Default.ProfileFile);
        // Every window speaks the language of the profile; without one (first start) English.
        L.SetLanguage(profile?.Language ?? L.English);
        Text.UiLanguage.ApplyCulture();
        ThemeManager.Start(this, profile);
        switch (Mode)
        {
            case StartMode.Watch:
                Watcher.Run(this, WatchPid, WatchStartTicks);
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
            MessageBox.Show(L.F("App.AudioServiceDown", ex.Message),
                "Soundboard", MessageBoxButton.OK, MessageBoxImage.Warning);
            Instance?.Dispose();
            Instance = null;
            Shutdown();
            return;
        }
        _window = new Views.MainWindow(_vm);
        MainWindow = _window;
        _tray = new TrayIcon(this, _vm);
        _vm.KeyWithoutEffect += OnKeyWithoutEffect;
        _vm.QuietInfo += OnQuietInfo;
        Instance?.ListenForShow(() => Dispatcher.BeginInvoke(ShowMain));
        Watcher.StartForThisProcess();
        if (!StartInTray) _window.Show();
        _vm.UpdateRestartRequested += (_, _) => ExitNow(UpdateAtExit.InstallAndRestart);
        _vm.StartUpdates();
    }

    private void StartUninstall()
    {
        var window = new Views.UninstallWindow(
            Core.Config.AppPaths.Default,
            ReadStateSafe,
            new Core.SystemState.PolicyConfigWriter(),
            SingleInstance.IsRunning,
            new Autostart(new RegistryRunKey(), Environment.ProcessPath),
            Cli.Changes(Core.Config.AppPaths.Default));
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

    /// <summary>"Remove everything" from the tray: open the removal guide and end this copy.</summary>
    public void RequestUninstall()
    {
        if (_vm is null || _exiting) return;
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        var text = L.Get(_vm.CableRunning ? "App.UninstallAskCable" : "App.UninstallAsk");
        if (!Views.ConfirmWindow.Ask(_window, L.Get("App.UninstallTitle"), L.Get("App.UninstallHeading"), text,
                L.Get("App.UninstallGo"), L.Get("Exit.Keep")))
            return;
        ExitNow(UpdateAtExit.Skip);
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

    /// <summary>At most one notification per reason every 10 seconds.</summary>
    private readonly HintThrottle _keyHints = new(TimeSpan.FromSeconds(10));

    /// <summary>
    /// A global key did nothing. In front, the window's notice says it; behind a game or another
    /// window a quiet Windows notification does, so the key does not seem dead.
    /// </summary>
    private void OnKeyWithoutEffect(string reason, string text)
    {
        if (_window is { IsVisible: true, IsActive: true }) return;
        if (!_keyHints.ShouldShow(reason, DateTime.UtcNow)) return;
        _tray?.ShowHint(L.Get("Hint.Title"), text);
    }

    /// <summary>
    /// Something the app did by itself (e.g. set a default device back). In front, the settings say
    /// it; behind other windows a silent Windows notification does.
    /// </summary>
    private void OnQuietInfo(string title, string text)
    {
        if (_window is { IsVisible: true, IsActive: true }) return;
        _tray?.ShowHint(title, text);
    }

    /// <summary>The window was closed with X: keep running in the tray, say so once.</summary>
    public void OnHiddenToTray()
    {
        if (_vm is null || _vm.TrayHintShown) return;
        _vm.TrayHintShown = true;
        _tray?.ShowHint(L.Get("Tray.StillRunningTitle"), L.Get("Tray.StillRunning"));
    }

    /// <summary>"Quit" from the tray: explain what Discord gets and how the audio state looks.</summary>
    public void RequestExit()
    {
        if (_vm is null || _exiting) return;
        if (Views.ConfirmWindow.Ask(_window, L.Get("App.QuitTitle"), L.Get("Exit.Question"), _vm.ExitQuestion(),
                L.Get("Exit.Quit"), L.Get("Exit.Keep")))
            ExitNow();
    }

    /// <summary>What happens with a fetched update when the app ends.</summary>
    public enum UpdateAtExit
    {
        /// <summary>Install it quietly if automatic updates are on (normal quit).</summary>
        IfEnabled,
        /// <summary>The user clicked the update: install and start again.</summary>
        InstallAndRestart,
        /// <summary>Leave it (removal guide follows).</summary>
        Skip,
    }

    /// <summary>Ends without asking (after confirmation or for the uninstall guide).</summary>
    public void ExitNow(UpdateAtExit update = UpdateAtExit.IfEnabled)
    {
        if (_exiting) return;
        _exiting = true;
        string? setup = update == UpdateAtExit.Skip ? null : _vm?.SetupToRunAtExit(update == UpdateAtExit.InstallAndRestart);
        _vm?.Dispose();
        _tray?.Dispose();
        _window?.Close();
        Instance?.Dispose();
        Instance = null;
        // After the single-instance mutex is gone; the setup itself waits until every Soundboard
        // process (this one and its watcher) has ended before it replaces the program file.
        if (setup is not null) StartSetup(setup, restart: update == UpdateAtExit.InstallAndRestart);
        Shutdown();
    }

    private static void StartSetup(string setup, bool restart)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(setup) { UseShellExecute = false };
        foreach (var a in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/UPDATE=1" }) psi.ArgumentList.Add(a);
        if (restart) psi.ArgumentList.Add("/RELAUNCH=1");
        try { System.Diagnostics.Process.Start(psi)?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    /// <summary>
    /// Windows signs out or shuts down and may end this process without a normal exit: the
    /// Windows settings changed for this run go back first, so the next sign-in starts as usual.
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _vm?.UndoSessionChanges();
        base.OnSessionEnding(e);
    }

    /// <summary>Unexpected errors become a notice instead of closing the program.</summary>
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (MainWindow?.DataContext is MainViewModel vm)
        {
            var message = e.Exception.Message;
            vm.ShowNotice("unexpected", () => L.F("App.Unexpected", message));
            e.Handled = true;
        }
    }
}
