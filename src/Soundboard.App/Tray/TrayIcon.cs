using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Soundboard.App.ViewModels;
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
    private readonly GeneratedIconSource _iconSource;
    private readonly List<(MenuItem Item, string Key)> _texts = [];

    public TrayIcon(App app, MainViewModel vm)
    {
        _vm = vm;
        var background = new SolidColorBrush(Themes.ThemeManager.ToColor(Core.Design.AccentPalette.Resolve(Themes.ThemeManager.AccentId, Themes.ThemeManager.CustomAccent, false).Fill));
        background.Freeze();

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

        _icon = new TaskbarIcon
        {
            ToolTipText = Tooltip(),
            IconSource = _iconSource = new GeneratedIconSource
            {
                Text = "♪",
                Foreground = Brushes.White,
                Background = background,
                FontSize = 46,
                FontWeight = System.Windows.FontWeights.Bold,
            },
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(app.ShowMain),
            ContextMenu = menu,
        };
        // false: no Windows "efficiency mode" for this process — it would slow the audio threads.
        _icon.ForceCreate(false);

        vm.PropertyChanged += OnVmChanged;
        Themes.ThemeManager.Changed += OnThemeChanged;
        L.Changed += OnLanguageChanged;
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

    /// <summary>The note symbol follows the accent colour chosen in the settings.</summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        var b = new SolidColorBrush(Themes.ThemeManager.ToColor(Core.Design.AccentPalette.Resolve(Themes.ThemeManager.AccentId, Themes.ThemeManager.CustomAccent, false).Fill));
        b.Freeze();
        _iconSource.Background = b;
    }

    /// <summary>The menu follows the interface language.</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var (item, key) in _texts) item.Header = L.Get(key);
    }

    public void Dispose()
    {
        L.Changed -= OnLanguageChanged;
        Themes.ThemeManager.Changed -= OnThemeChanged;
        _vm.PropertyChanged -= OnVmChanged;
        _icon.Dispose();
    }
}
