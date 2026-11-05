using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Soundboard.App.Views;

/// <summary>
/// The waveform of a sound with two edges, start and end, to drag. Outside the kept part the wave is
/// drawn muted under a veil. A click into the wave moves the nearer edge there. With the keyboard:
/// Tab to the control, Left/Right moves the active edge by 10 ms (with Shift 100 ms), Space switches
/// the active edge. Nothing here changes size while it is used.
/// </summary>
public sealed class WaveTrimView : FrameworkElement
{
    private const double Grab = 8;      // px on either side of an edge that grab it
    private const double HandleW = 3;   // px

    private bool _dragging;

    public WaveTrimView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.IBeam;
        SnapsToDevicePixels = true;
    }

    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(nameof(Peaks), typeof(float[]), typeof(WaveTrimView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(nameof(Duration), typeof(double), typeof(WaveTrimView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StartProperty = DependencyProperty.Register(nameof(Start), typeof(double), typeof(WaveTrimView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EndProperty = DependencyProperty.Register(nameof(End), typeof(double), typeof(WaveTrimView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PlayheadProperty = DependencyProperty.Register(nameof(Playhead), typeof(double?), typeof(WaveTrimView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty WaveBrushProperty = DependencyProperty.Register(nameof(WaveBrush), typeof(Brush), typeof(WaveTrimView),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MutedBrushProperty = DependencyProperty.Register(nameof(MutedBrush), typeof(Brush), typeof(WaveTrimView),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty VeilBrushProperty = DependencyProperty.Register(nameof(VeilBrush), typeof(Brush), typeof(WaveTrimView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HandleBrushProperty = DependencyProperty.Register(nameof(HandleBrush), typeof(Brush), typeof(WaveTrimView),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(WaveTrimView),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Peak per column of the whole sound, 0..1.</summary>
    public float[]? Peaks { get => (float[]?)GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }

    /// <summary>Length of the whole sound in seconds.</summary>
    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }

    public double Start { get => (double)GetValue(StartProperty); set => SetValue(StartProperty, value); }

    public double End { get => (double)GetValue(EndProperty); set => SetValue(EndProperty, value); }

    /// <summary>Where listening is now (seconds into the file), null when nothing plays.</summary>
    public double? Playhead { get => (double?)GetValue(PlayheadProperty); set => SetValue(PlayheadProperty, value); }

    public Brush WaveBrush { get => (Brush)GetValue(WaveBrushProperty); set => SetValue(WaveBrushProperty, value); }
    public Brush MutedBrush { get => (Brush)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public Brush? VeilBrush { get => (Brush?)GetValue(VeilBrushProperty); set => SetValue(VeilBrushProperty, value); }
    public Brush HandleBrush { get => (Brush)GetValue(HandleBrushProperty); set => SetValue(HandleBrushProperty, value); }
    public Brush Background { get => (Brush)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    /// <summary>Which edge the keyboard and a drag move: true = end.</summary>
    public bool EndActive { get; private set; }

    /// <summary>Start or end was moved by the user.</summary>
    public event EventHandler? RangeChanged;

    private double X(double seconds) => Duration <= 0 ? 0 : seconds / Duration * ActualWidth;

    private double Seconds(double x) => Duration <= 0 ? 0 : Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1) * Duration;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        if (w <= 0 || h <= 0 || Duration <= 0) return;

        double xs = X(Start), xe = X(End), mid = h / 2;
        var peaks = Peaks;
        if (peaks is { Length: > 0 })
        {
            // One bar per 2 px: the highest peak of the columns it covers.
            for (double x = 0; x < w; x += 2)
            {
                int c0 = (int)(x / w * peaks.Length);
                int c1 = Math.Max(c0 + 1, (int)((x + 2) / w * peaks.Length));
                float p = 0;
                for (int c = c0; c < c1 && c < peaks.Length; c++) p = Math.Max(p, peaks[c]);
                double half = Math.Max(0.5, p * (mid - 4));
                var brush = x + 1 >= xs && x <= xe ? WaveBrush : MutedBrush;
                dc.DrawRectangle(brush, null, new Rect(x, mid - half, 1.5, half * 2));
            }
        }

        if (VeilBrush is { } veil)
        {
            if (xs > 0) dc.DrawRectangle(veil, null, new Rect(0, 0, xs, h));
            if (xe < w) dc.DrawRectangle(veil, null, new Rect(xe, 0, w - xe, h));
        }

        if (Playhead is { } ph && ph >= Start && ph <= End)
            dc.DrawRectangle(HandleBrush, null, new Rect(Math.Round(X(ph)), 0, 1, h));

        DrawEdge(dc, xs, h, isEnd: false);
        DrawEdge(dc, xe, h, isEnd: true);
    }

    /// <summary>An edge: a line over the full height with a small tab at the top, pointing inwards.</summary>
    private void DrawEdge(DrawingContext dc, double x, double h, bool isEnd)
    {
        double left = isEnd ? x - HandleW : x;
        left = Math.Clamp(left, 0, Math.Max(0, ActualWidth - HandleW));
        dc.DrawRectangle(HandleBrush, null, new Rect(Math.Round(left), 0, HandleW, h));
        double tabX = isEnd ? left - 9 + HandleW : left;
        dc.DrawRoundedRectangle(HandleBrush, null, new Rect(Math.Round(tabX), 0, 9, 14), 2, 2);
        if (IsKeyboardFocused && EndActive == isEnd)
            dc.DrawRoundedRectangle(null, new Pen(HandleBrush, 1), new Rect(Math.Round(tabX) - 2.5, -1.5 + 0.5, 14, 18), 3, 3);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        double x = e.GetPosition(this).X;
        if (_dragging)
        {
            MoveActive(Seconds(x));
            return;
        }
        Cursor = Math.Abs(x - X(Start)) <= Grab || Math.Abs(x - X(End)) <= Grab ? Cursors.SizeWE : Cursors.IBeam;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        double x = e.GetPosition(this).X;
        double ds = Math.Abs(x - X(Start)), de = Math.Abs(x - X(End));
        // The nearer edge; on a tie the one on the side of the click.
        EndActive = de < ds || (de == ds && x > X(End));
        _dragging = true;
        CaptureMouse();
        MoveActive(Seconds(x));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.01;
        switch (e.Key)
        {
            case Key.Left: MoveActive((EndActive ? End : Start) - step); break;
            case Key.Right: MoveActive((EndActive ? End : Start) + step); break;
            case Key.Space: EndActive = !EndActive; InvalidateVisual(); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

    /// <summary>Moves the active edge, kept inside the sound and at least the shortest length from the other.</summary>
    private void MoveActive(double seconds)
    {
        double min = Core.Audio.SoundTrim.MinLength;
        if (EndActive) End = Math.Clamp(seconds, Math.Min(Duration, Start + min), Duration);
        else Start = Math.Clamp(seconds, 0, Math.Max(0, End - min));
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }
}
