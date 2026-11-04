using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Soundboard.App.ViewModels;
using Soundboard.Core.Audio;
using Soundboard.Core.Config;
using Soundboard.Core.SystemState;

namespace Soundboard.App.Views;

/// <summary>
/// Guided removal (<c>Soundboard.exe --uninstall</c>, also from the tray menu): check the
/// audio state and offer "Standardgerät zurückstellen", delete the app's own data after a question (sounds
/// kept unless unticked), explain how to remove the cable driver, and compare at the end.
/// Snapshot and the noted Discord microphone are read once at the start, so they are still
/// there for the last step after the data has been deleted.
/// </summary>
public partial class UninstallWindow : Window
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private readonly AppPaths _paths;
    private readonly Func<AudioStateReport> _readState;
    private readonly IDefaultEndpointWriter _writer;
    private readonly Func<bool> _isRunning;
    private readonly AudioSnapshot? _snapshot;
    private readonly string? _previousMic;
    private readonly DispatcherTimer _runningTimer;

    public UninstallWindow(AppPaths paths, Func<AudioStateReport> readState, IDefaultEndpointWriter writer, Func<bool> isRunning)
    {
        InitializeComponent();
        _paths = paths;
        _readState = readState;
        _writer = writer;
        _isRunning = isRunning;

        try { _snapshot = SnapshotStore.Load(paths.SnapshotFile); }
        catch (SnapshotException) { _snapshot = null; }
        _previousMic = ProfileStore.Load(paths.ProfileFile).Profile.DiscordPreviousMic;

        DiscordText.Text = "Zeigt Discord noch auf das entfernte Kabel, nimmt es von selbst „Default“ – das ist dein Windows-Standardmikrofon, das das Soundboard nie verändert hat. "
            + (string.IsNullOrWhiteSpace(_previousMic)
                ? "Hattest du vorher ein bestimmtes Mikrofon gewählt: " + MainViewModel.DiscordWayBack(null)
                : "Du hattest notiert, dass vorher „" + _previousMic + "“ gewählt war: " + MainViewModel.DiscordWayBack(_previousMic));

        _runningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _runningTimer.Tick += (_, _) => UpdateRunning();
        _runningTimer.Start();
        Closed += (_, _) => _runningTimer.Stop();

        UpdateRunning();
        UpdateTraces();
        Compare();
    }

    private void UpdateRunning() =>
        RunningBox.Visibility = _isRunning() ? Visibility.Visible : Visibility.Collapsed;

    private void Compare()
    {
        var now = _readState();
        if (_snapshot is null)
        {
            StateText.Text = "Es gibt keine Sicherung des Audio-Zustands. Vergleichen und zurückstellen ist deshalb nicht möglich; geändert wurde vom Soundboard ohnehin nichts.";
            RestoreButton.Visibility = Visibility.Collapsed;
            Table.ItemsSource = null;
            CompareText.Text = "";
            return;
        }

        var cmp = SnapshotService.Compare(_snapshot, now);
        string when = _snapshot.TakenAt.ToString("dd.MM.yyyy, HH:mm", De);
        StateText.Text = cmp.DefaultsChanged
            ? $"Seit der Sicherung vom {when} weicht ein Windows-Standardgerät ab: {string.Join("; ", cmp.DefaultChangeLines)}. „Standardgerät zurückstellen“ stellt es zurück."
            : $"Die Windows-Standardgeräte sind wie in der Sicherung vom {when}. Hier ist nichts zu tun.";
        RestoreButton.Visibility = cmp.DefaultsChanged ? Visibility.Visible : Visibility.Collapsed;
        Table.ItemsSource = cmp.Rows;
        CompareText.Text = cmp.IsIdentical
            ? "Alles wie gesichert."
            : $"{cmp.DifferenceCount} Abweichung(en). Neu oder fehlend ist normal, solange das Kabel noch da ist oder ein Gerät abgezogen wurde.";
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null) return;
        var result = SnapshotService.Restore(_snapshot, _readState(), _writer);
        var sb = new StringBuilder(result.RestoredCount == 1 ? "1 Standardgerät zurückgestellt." : $"{result.RestoredCount} Standardgeräte zurückgestellt.");
        if (!result.AllGood) sb.Append(" Nicht alles ging automatisch. Von Hand:").Append(Environment.NewLine).Append(result.ManualSteps());
        RestoreText.Text = sb.ToString();
        RestoreText.Visibility = Visibility.Visible;
        Compare();
    }

    private void OnKeepChanged(object sender, RoutedEventArgs e) => UpdateTraces();

    private void UpdateTraces()
    {
        if (TracesText is null || _paths is null) return; // during InitializeComponent
        bool keep = KeepSounds.IsChecked == true;
        var items = OwnTraces.List(_paths, keep);
        var autostart = Autostart.Read();
        var sb = new StringBuilder();
        if (items.Count == 0 && autostart is null)
        {
            sb.Append("Es liegen keine Daten des Soundboards (mehr) vor.");
        }
        else
        {
            sb.Append("Gelöscht wird im Datenordner ").Append(_paths.DataDir).Append(':');
            foreach (var group in items.GroupBy(i => i.Description)) sb.Append(Environment.NewLine).Append("•  ").Append(group.Key);
            if (autostart is not null) sb.Append(Environment.NewLine).Append("•  Eintrag „mit Windows starten“");
            if (keep && (Directory.Exists(_paths.SoundsDir) || Directory.Exists(_paths.ImagesDir))) sb.Append(Environment.NewLine).Append("Sound- und Bilder-Ordner bleiben.");
        }
        TracesText.Text = sb.ToString();
    }

    private void OnRemoveTraces(object sender, RoutedEventArgs e)
    {
        if (_isRunning())
        {
            RemoveText.Text = "Das Soundboard läuft noch und hält seine Dateien offen. Beende es zuerst über das Notensymbol im Infobereich → „Beenden …“, dann noch einmal klicken.";
            RemoveText.Visibility = Visibility.Visible;
            return;
        }
        bool keep = KeepSounds.IsChecked == true;
        var question = keep
            ? "Einstellungen, Sicherungen und Test-Aufnahmen des Soundboards löschen? Sound- und Bilder-Ordner bleiben. Das lässt sich nicht rückgängig machen."
            : "Einstellungen, Sicherungen, Test-Aufnahmen UND alle Sounds und Pad-Bilder des Soundboards löschen? Das lässt sich nicht rückgängig machen.";
        if (MessageBox.Show(this, question, "Eigene Daten löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var removed = OwnTraces.Remove(_paths, keep);
        var sb = new StringBuilder();
        int ok = removed.Count(r => r.Removed);
        sb.Append(ok == 1 ? "1 Eintrag gelöscht." : $"{ok} Einträge gelöscht.");
        if (Autostart.Read() is not null)
        {
            var err = Autostart.Remove();
            sb.Append(err is null ? " Der Eintrag „mit Windows starten“ ist entfernt." : $" Der Eintrag „mit Windows starten“ ließ sich nicht entfernen ({err}).");
        }
        foreach (var f in removed.Where(r => !r.Removed))
            sb.Append(Environment.NewLine).Append($"Nicht gelöscht: {f.Path} ({f.Error}) – bitte von Hand löschen.");
        RemoveText.Text = sb.ToString();
        RemoveText.Visibility = Visibility.Visible;
        UpdateTraces();
    }

    private void OnDeviceManager(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private void OnCompare(object sender, RoutedEventArgs e) => Compare();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
