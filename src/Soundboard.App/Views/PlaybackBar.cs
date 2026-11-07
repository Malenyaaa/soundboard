using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using Soundboard.Core.Audio;

namespace Soundboard.App.Views;

/// <summary>
/// The bar under the pads for the sound started last: its waveform as thin vertical strokes, under it a
/// thin track with the played part and a round knob. A click jumps there, dragging the knob (or anywhere
/// on the bar) winds back and forth. Keyboard: Left/Right 5 % of the sound, Home/End. Nothing here
/// changes size or place while it is used; hovering only draws a faint line where a click would jump.
/// </summary>
public sealed class PlaybackBar : FrameworkElement
{
    private const double Step = 3;        // px from stroke to stroke
    private const double StrokeW = 1.5;   // px
    private const double TrackH = 3;      // px
    private const double KnobR = 6;       // px
    private const double TrackZone = 16;  // px at the bottom: track and knob

    private float[]? _peaks;
    private SoundClip? _peaksClip;
    private int _peaksColumns;
    private double? _dragAt;   // 0..1 while the mouse holds the bar
    private double? _hoverAt;  // 0..1 under the mouse

    public PlaybackBar()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
    }

    public static readonly DependencyProperty SoundProperty = DependencyProperty.Register(nameof(Sound), typeof(SoundClip), typeof(PlaybackBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(PlaybackBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PlayedBrushProperty = DependencyProperty.Register(nameof(PlayedBrush), typeof(Brush), typeof(PlaybackBar),
        new FrameworkPropertyMetadata(Brushes.Teal, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RestBrushProperty = DependencyProperty.Register(nameof(RestBrush), typeof(Brush), typeof(PlaybackBar),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(PlaybackBar),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty KnobRimProperty = DependencyProperty.Register(nameof(KnobRim), typeof(Brush), typeof(PlaybackBar),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FocusBrushProperty = DependencyProperty.Register(nameof(FocusBrush), typeof(Brush), typeof(PlaybackBar),
        new FrameworkPropertyMetadata(Brushes.Teal, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The sound shown; null before the first one (then the bar rests empty).</summary>
    public SoundClip? Sound { get => (SoundClip?)GetValue(SoundProperty); set => SetValue(SoundProperty, value); }

    /// <summary>Played part 0..1.</summary>
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    /// <summary>Played strokes, played track and the knob.</summary>
    public Brush PlayedBrush { get => (Brush)GetValue(PlayedBrushProperty); set => SetValue(PlayedBrushProperty, value); }

    /// <summary>Strokes not played yet.</summary>
    public Brush RestBrush { get => (Brush)GetValue(RestBrushProperty); set => SetValue(RestBrushProperty, value); }

    /// <summary>The track not played yet.</summary>
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    /// <summary>Ring around the knob (the bar's background), so it stands off the strokes.</summary>
    public Brush KnobRim { get => (Brush)GetValue(KnobRimProperty); set => SetValue(KnobRimProperty, value); }

    public Brush FocusBrush { get => (Brush)GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }

    /// <summary>The user asks to jump to this place (0..1): on a click, while dragging, and by key.</summary>
    public event Action<double>? SeekRequested;

    /// <summary>Where the knob stands: under the mouse while dragging, else the progress.</summary>
    public double Shown => _dragAt ?? Math.Clamp(Progress, 0, 1);

    private double Inner => Math.Max(1, ActualWidth - 2 * KnobR);

    private double X(double at) => KnobR + at * Inner;

    /// <summary>Place 0..1 for a mouse position (the knob's half-width is margin on both ends).</summary>
    public double At(double x) => Math.Clamp((x - KnobR) / Inner, 0, 1);

    protected override Size MeasureOverride(Size available) => new(0, 38);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // the whole bar takes the mouse
        if (w <= 2 * KnobR || h <= TrackZone) return;
        var clip = Sound;
        double at = clip is null ? 0 : Shown;
        double playedX = X(at);

        // Waveform: one thin stroke every few pixels, centred in the space above the track.
        double waveH = h - TrackZone, mid = waveH / 2;
        int columns = Math.Max(1, (int)(Inner / Step));
        var peaks = clip is null ? null : PeaksFor(clip, columns);
        for (int i = 0; i < columns; i++)
        {
            double x = KnobR + i * Step;
            double p = peaks is null ? 0 : peaks[Math.Min(i, peaks.Length - 1)];
            double half = Math.Max(1, p * (mid - 1));
            dc.DrawRectangle(clip is not null && x + StrokeW <= playedX ? PlayedBrush : RestBrush, null,
                new Rect(x, mid - half, StrokeW, half * 2));
        }

        // Track with the played part, and the knob.
        double ty = h - TrackZone / 2 - TrackH / 2;
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(KnobR, ty, Inner, TrackH), TrackH / 2, TrackH / 2);
        if (clip is null) return;
        if (playedX > KnobR)
            dc.DrawRoundedRectangle(PlayedBrush, null, new Rect(KnobR, ty, playedX - KnobR, TrackH), TrackH / 2, TrackH / 2);
        if (_hoverAt is { } hover && _dragAt is null)
            dc.DrawRectangle(RestBrush, null, new Rect(Math.Round(X(hover)), 0, 1, waveH));
        var centre = new Point(playedX, ty + TrackH / 2);
        dc.DrawEllipse(KnobRim, null, centre, KnobR + 2, KnobR + 2);
        dc.DrawEllipse(PlayedBrush, null, centre, KnobR, KnobR);
        if (IsKeyboardFocused)
            dc.DrawEllipse(null, new Pen(FocusBrush, 2), centre, KnobR + 4, KnobR + 4);
    }

    private float[] PeaksFor(SoundClip clip, int columns)
    {
        if (_peaks is null || !ReferenceEquals(clip, _peaksClip) || columns != _peaksColumns)
        {
            var raw = SoundTrim.Peaks(clip, columns);
            // Quiet sounds still show their shape: scale so the loudest stroke fills the height.
            float max = raw.Length == 0 ? 0 : raw.Max();
            _peaks = max > 0.001f ? raw.Select(p => p / max).ToArray() : raw;
            _peaksClip = clip;
            _peaksColumns = columns;
        }
        return _peaks;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Sound is null) return;
        Focus();
        CaptureMouse();
        _dragAt = At(e.GetPosition(this).X);
        SeekRequested?.Invoke(_dragAt.Value);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        double at = At(e.GetPosition(this).X);
        if (_dragAt is { } was && IsMouseCaptured)
        {
            if (Math.Abs(at - was) * Inner < 1) return; // less than a pixel: no new jump
            _dragAt = at;
            SeekRequested?.Invoke(at);
        }
        _hoverAt = Sound is null ? null : at;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragAt is null) return;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragAt = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverAt = null;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Sound is null || Keyboard.Modifiers != ModifierKeys.None) return;
        double? to = e.Key switch
        {
            Key.Left => Shown - 0.05,
            Key.Right => Shown + 0.05,
            Key.Home => 0,
            Key.End => 0.999,
            _ => null,
        };
        if (to is null) return;
        SeekRequested?.Invoke(Math.Clamp(to.Value, 0, 1));
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
