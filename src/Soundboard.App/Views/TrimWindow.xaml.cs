using System.Windows;
using System.Windows.Threading;
using Soundboard.App.ViewModels;
using Soundboard.Core.Audio;
using Soundboard.Core.Text;

namespace Soundboard.App.Views;

/// <summary>
/// Cut a pad to a part of its sound. The pad keeps start and end; the file is not changed. Listening
/// plays the chosen part on the monitor only. "Apply" sets the cut (Ctrl+Z on the main window takes
/// it back), Esc or "Cancel" leaves the pad as it was.
/// </summary>
public partial class TrimWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly TileViewModel _tile;
    private readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private double _playFrom, _playTo;
    private bool _listening;

    public TrimWindow(MainViewModel vm, TileViewModel tile)
    {
        InitializeComponent();
        _vm = vm;
        _tile = tile;
        var full = tile.FullClip!;
        double duration = full.Duration.TotalSeconds;

        Title = L.Get("Trim.Title");
        HeadingText.Text = L.F("Trim.Heading", tile.Name);
        HintText.Text = L.Get("Trim.Hint");
        StartLabel.Text = L.Get("Trim.Start");
        EndLabel.Text = L.Get("Trim.End");
        LengthLabel.Text = L.Get("Trim.Length");
        ApplyButton.Content = L.Get("Trim.Apply");
        CancelButton.Content = L.Get("Common.Cancel");
        WholeButton.Content = L.Get("Trim.Whole");
        ListenButton.Content = L.Get("Trim.Listen");

        Wave.Duration = duration;
        Wave.Peaks = SoundTrim.Peaks(full, 1600);
        Wave.Start = tile.Config.TrimStart ?? 0;
        Wave.End = tile.Config.TrimEnd ?? duration;
        Wave.RangeChanged += (_, _) => OnRangeChanged();
        UpdateTimes();

        _follow.Tick += (_, _) => FollowPlayhead();
        Closed += (_, _) =>
        {
            _follow.Stop();
            _vm.StopTrimPreview();
        };
        Loaded += (_, _) => Wave.Focus();
        Text.UiLanguage.Attach(this);
    }

    public static void Show(Window owner, MainViewModel vm, TileViewModel tile)
    {
        var w = new TrimWindow(vm, tile) { Owner = owner };
        w.ShowDialog();
    }

    private void OnRangeChanged()
    {
        UpdateTimes();
        // While listening, a moved edge starts the part again from its new start.
        if (_listening) StartListening();
    }

    private void UpdateTimes()
    {
        StartValue.Text = Seconds(Wave.Start);
        EndValue.Text = Seconds(Wave.End);
        LengthValue.Text = Seconds(Wave.End - Wave.Start);
    }

    private static string Seconds(double s) => L.F("Trim.Seconds", s);

    private void OnListen(object sender, RoutedEventArgs e)
    {
        if (_listening) StopListening();
        else StartListening();
    }

    private void StartListening()
    {
        _playFrom = Wave.Start;
        _playTo = Wave.End;
        if (_vm.PreviewTrim(_tile, _playFrom, _playTo) is null)
        {
            // Not still: say why and what helps.
            StopListening();
            ListenNoteText.Text = L.Get("Trim.NoMonitor");
            ListenNote.Visibility = Visibility.Visible;
            return;
        }
        ListenNote.Visibility = Visibility.Collapsed;
        _listening = true;
        ListenButton.Content = L.Get("Trim.StopListening");
        _follow.Tag = DateTime.Now;
        _follow.Start();
    }

    private void StopListening()
    {
        _vm.StopTrimPreview();
        _listening = false;
        _follow.Stop();
        Wave.Playhead = null;
        ListenButton.Content = L.Get("Trim.Listen");
    }

    private void FollowPlayhead()
    {
        if (_vm.TrimPreviewProgress() is { } p)
        {
            Wave.Playhead = _playFrom + Math.Clamp(p, 0, 1) * (_playTo - _playFrom);
            return;
        }
        // The part has played to its end (a short grace for the start, before the player has picked it up).
        if (_follow.Tag is DateTime started && DateTime.Now - started < TimeSpan.FromMilliseconds(300)) return;
        StopListening();
    }

    private void OnWhole(object sender, RoutedEventArgs e)
    {
        Wave.Start = 0;
        Wave.End = Wave.Duration;
        OnRangeChanged();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        _vm.SetTileTrim(_tile, Wave.Start, Wave.End);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
