using System.ComponentModel;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Soundboard.App.Icons;
using Soundboard.App.ViewModels;
using Soundboard.Core.Design;
using Soundboard.Core.Text;

namespace Soundboard.App.Tray;

/// <summary>
/// Symbol in the notification area. Closing the window keeps the app (and the microphone
/// path) running; quitting goes through the tray menu with a confirmation.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MainViewModel _vm;
    private readonly MenuItem _pauseItem;
    private readonly List<(MenuItem Item, string Key)> _texts = [];
    private (Rgb Accent, int Size) _drawn;

    public TrayIcon(App app, MainViewModel vm)
    {
        _vm = vm;

        var menu = new ContextMenu();
        menu.Items.Add(Item("Tray.Open", app.ShowMain));
        menu.Items.Add(Item("Tray.Stop", () => vm.StopAllCommand.Execute(null)));
        _pauseItem = Item("Keys.Pause", () => vm.HotkeysPaused = !vm.HotkeysPaused);
        _pauseItem.IsCheckable = true;
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Tab.Setup", () => { app.ShowMain(); vm.OpenSetupCommand.Execute(null); }));
        menu.Items.Add(Item("Tray.Uninstall", app.RequestUninstall));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Tray.Quit", app.RequestExit));

        _drawn = Wanted();
        _icon = new TaskbarIcon
        {
            ToolTipText = Tooltip(),
            Icon = TrayIconImage.Create(_drawn.Accent, _drawn.Size),
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(app.ShowMain),
            ContextMenu = menu,
        };
        // false: no Windows "efficiency mode" for this process — it would slow the audio threads.
        _icon.ForceCreate(false);

        vm.PropertyChanged += OnVmChanged;
        Themes.ThemeManager.Changed += OnThemeChanged;
        L.Changed += OnLanguageChanged;
        // H.NotifyIcon only redraws an IconSource on a DPI change; our own icon is redrawn here.
        // A system-aware process rarely gets WM_DPICHANGED, so also look again when the display
        // settings change (scaling) and when Explorer recreates the taskbar.
        _icon.TrayIcon.MessageWindow.DpiChanged += OnDisplayChanged;
        _icon.TrayIcon.MessageWindow.TaskbarCreated += OnDisplayChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    /// <summary>Accent (as the light mode fills it, like the app icon) and the tray size for the taskbar's DPI.</summary>
    private static (Rgb Accent, int Size) Wanted() =>
        (AccentPalette.Resolve(Themes.ThemeManager.AccentId, Themes.ThemeManager.CustomAccent, false).Fill, TrayIconImage.CurrentSize());

    /// <summary>Draws the symbol again when accent or size changed. Setting Icon disposes the previous one (and its HICON).</summary>
    private void Redraw()
    {
        if (_icon.IsDisposed) return;
        var wanted = Wanted();
        if (wanted == _drawn) return;
        _drawn = wanted;
        _icon.Icon = TrayIconImage.Create(wanted.Accent, wanted.Size);
    }

    private MenuItem Item(string key, Action action)
    {
        var item = new MenuItem { Header = L.Get(key) };
        _texts.Add((item, key));
        item.Click += (_, _) => action();
        return item;
    }

    private string Tooltip() => $"Soundboard – {_vm.StatusCable}";

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StatusCable)) _icon.ToolTipText = Tooltip();
        if (e.PropertyName == nameof(MainViewModel.HotkeysPaused)) _pauseItem.IsChecked = _vm.HotkeysPaused;
    }

    /// <summary>Short Windows notification (no sound).</summary>
    public void ShowHint(string title, string text) =>
        _icon.ShowNotification(title, text, sound: false);

    /// <summary>The symbol follows the accent colour chosen in the settings.</summary>
    private void OnThemeChanged(object? sender, EventArgs e) => Redraw();

    // Raised on other threads (SystemEvents) or inside the message window's handler: redraw later on the UI thread.
    private void OnDisplayChanged(object? sender, EventArgs e) => _icon.Dispatcher.BeginInvoke(Redraw);

    /// <summary>The menu follows the interface language.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var (item, key) in _texts) item.Header = L.Get(key);
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _icon.TrayIcon.MessageWindow.TaskbarCreated -= OnDisplayChanged;
        _icon.TrayIcon.MessageWindow.DpiChanged -= OnDisplayChanged;
        L.Changed -= OnLanguageChanged;
        Themes.ThemeManager.Changed -= OnThemeChanged;
        _vm.PropertyChanged -= OnVmChanged;
        var last = _icon.Icon;
        _icon.Dispose();
        last?.Dispose();
    }
}
