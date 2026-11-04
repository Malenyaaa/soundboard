using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Soundboard.Core.Design;

namespace Soundboard.App.Views;

/// <summary>
/// Colour picker: a field for saturation (left → right) and brightness (top → bottom) in the
/// current hue, and a bar for the hue. Both are dragged with the mouse or moved with the arrow
/// keys (Shift: bigger steps). <see cref="SelectedColor"/> changes while dragging.
/// </summary>
public sealed class ColorPicker : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(ColorPicker),
        new FrameworkPropertyMetadata(Colors.SteelBlue, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    public static readonly DependencyProperty AreaNameProperty = DependencyProperty.Register(
        nameof(AreaName), typeof(string), typeof(ColorPicker), new PropertyMetadata("", (d, e) => AutomationProperties.SetName(((ColorPicker)d)._area, (string)e.NewValue)));

    public static readonly DependencyProperty HueNameProperty = DependencyProperty.Register(
        nameof(HueName), typeof(string), typeof(ColorPicker), new PropertyMetadata("", (d, e) => AutomationProperties.SetName(((ColorPicker)d)._hue, (string)e.NewValue)));

    private const double AreaWidth = 240, AreaHeight = 150, HueWidth = 22, Knob = 16;

    private readonly Border _area;
    private readonly Border _hue;
    private readonly Rectangle _hueFill;
    private readonly Canvas _areaCanvas;
    private readonly Canvas _hueCanvas;
    private readonly Grid _areaKnob;
    private readonly Grid _hueKnob;

    private double _h, _s, _v;
    private bool _internal;

    public ColorPicker()
    {
        Focusable = false;

        // ---- saturation / brightness field
        _hueFill = new Rectangle();
        var white = new Rectangle
        {
            Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(1, 0)),
        };
        var black = new Rectangle
        {
            Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, new Point(0, 0), new Point(0, 1)),
        };
        _areaKnob = Ring(Knob, Knob, Knob / 2);
        _areaCanvas = new Canvas { ClipToBounds = false };
        _areaCanvas.Children.Add(_areaKnob);
        var areaGrid = new Grid();
        RoundClip.SetRadius(areaGrid, 5);
        areaGrid.Children.Add(_hueFill);
        areaGrid.Children.Add(white);
        areaGrid.Children.Add(black);
        var areaHost = new Grid();
        areaHost.Children.Add(areaGrid);
        areaHost.Children.Add(_areaCanvas);
        _area = Field(areaHost, AreaWidth, AreaHeight, Cursors.Cross);

        // ---- hue bar (red at the top, once around)
        var hues = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        for (int i = 0; i <= 6; i++)
        {
            var c = ColorMath.FromHsv(new Hsv(i * 60, 1, 1));
            hues.GradientStops.Add(new GradientStop(Color.FromRgb(c.R, c.G, c.B), i / 6.0));
        }
        hues.Freeze();
        var hueStrip = new Border { Background = hues };
        RoundClip.SetRadius(hueStrip, 5);
        _hueKnob = Ring(HueWidth + 6, 10, 3);
        _hueCanvas = new Canvas { ClipToBounds = false };
        _hueCanvas.Children.Add(_hueKnob);
        var hueHost = new Grid();
        hueHost.Children.Add(hueStrip);
        hueHost.Children.Add(_hueCanvas);
        _hue = Field(hueHost, HueWidth, AreaHeight, Cursors.SizeNS);

        var root = new StackPanel { Orientation = Orientation.Horizontal };
        root.Children.Add(_area);
        _hue.Margin = new Thickness(14, 0, 0, 0);
        root.Children.Add(_hue);
        Content = root;

        _area.MouseLeftButtonDown += (_, e) => { Grab(_area, e); FromArea(e.GetPosition(_area)); };
        _area.MouseMove += (_, e) => { if (_area.IsMouseCaptured) FromArea(e.GetPosition(_area)); };
        _area.MouseLeftButtonUp += (_, _) => _area.ReleaseMouseCapture();
        _area.KeyDown += OnAreaKey;
        _hue.MouseLeftButtonDown += (_, e) => { Grab(_hue, e); FromHue(e.GetPosition(_hue)); };
        _hue.MouseMove += (_, e) => { if (_hue.IsMouseCaptured) FromHue(e.GetPosition(_hue)); };
        _hue.MouseLeftButtonUp += (_, _) => _hue.ReleaseMouseCapture();
        _hue.KeyDown += OnHueKey;
        _area.SizeChanged += (_, _) => UpdateVisuals();
        _hue.SizeChanged += (_, _) => UpdateVisuals();

        ReadColor(SelectedColor);
        UpdateVisuals();
    }

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>Name of the field for screen readers ("Saturation and brightness").</summary>
    public string AreaName
    {
        get => (string)GetValue(AreaNameProperty);
        set => SetValue(AreaNameProperty, value);
    }

    /// <summary>Name of the hue bar for screen readers.</summary>
    public string HueName
    {
        get => (string)GetValue(HueNameProperty);
        set => SetValue(HueNameProperty, value);
    }

    /// <summary>Current hue (0..360), saturation and value (0..1) — the hue survives greys.</summary>
    public Hsv Current => new(_h, _s, _v);

    private static Border Field(UIElement child, double w, double h, Cursor cursor)
    {
        var b = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Focusable = true,
            Cursor = cursor,
            Child = child,
            Background = Brushes.Transparent,
        };
        b.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        b.SetResourceReference(FocusVisualStyleProperty, "FocusRing");
        KeyboardNavigation.SetIsTabStop(b, true);
        return b;
    }

    /// <summary>A knob that stays visible on any colour: dark outline, white ring, dark inner line.</summary>
    private static Grid Ring(double w, double h, double radius)
    {
        var g = new Grid { Width = w, Height = h, IsHitTestVisible = false };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x11, 0x13, 0x14)) });
        g.Children.Add(new Border { Margin = new Thickness(1), CornerRadius = new CornerRadius(Math.Max(0, radius - 1)), BorderThickness = new Thickness(2), BorderBrush = Brushes.White });
        g.Children.Add(new Border { Margin = new Thickness(3), CornerRadius = new CornerRadius(Math.Max(0, radius - 3)), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x11, 0x13, 0x14)) });
        return g;
    }

    private static void Grab(Border field, MouseButtonEventArgs e)
    {
        field.Focus();
        field.CaptureMouse();
        e.Handled = true;
    }

    private static double Inner(Border b, bool width) => Math.Max(1, (width ? b.ActualWidth : b.ActualHeight) - 2);

    private void FromArea(Point p)
    {
        _s = Math.Clamp((p.X - 1) / Inner(_area, true), 0, 1);
        _v = Math.Clamp(1 - (p.Y - 1) / Inner(_area, false), 0, 1);
        Commit();
    }

    private void FromHue(Point p)
    {
        _h = Math.Clamp((p.Y - 1) / Inner(_hue, false), 0, 1) * 360;
        if (_h >= 360) _h = 359.9;
        Commit();
    }

    private void OnAreaKey(object sender, KeyEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.01;
        switch (e.Key)
        {
            case Key.Left: _s = Math.Clamp(_s - step, 0, 1); break;
            case Key.Right: _s = Math.Clamp(_s + step, 0, 1); break;
            case Key.Up: _v = Math.Clamp(_v + step, 0, 1); break;
            case Key.Down: _v = Math.Clamp(_v - step, 0, 1); break;
            default: return;
        }
        e.Handled = true;
        Commit();
    }

    private void OnHueKey(object sender, KeyEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Up or Key.Left: _h = (_h - step + 360) % 360; break;
            case Key.Down or Key.Right: _h = (_h + step) % 360; break;
            default: return;
        }
        e.Handled = true;
        Commit();
    }

    private void Commit()
    {
        var c = ColorMath.FromHsv(new Hsv(_h, _s, _v));
        _internal = true;
        try { SelectedColor = Color.FromRgb(c.R, c.G, c.B); }
        finally { _internal = false; }
        UpdateVisuals();
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (ColorPicker)d;
        if (p._internal) return;
        p.ReadColor((Color)e.NewValue);
        p.UpdateVisuals();
    }

    /// <summary>Takes a colour from outside; a grey or black keeps the hue the picker had.</summary>
    private void ReadColor(Color c)
    {
        var hsv = ColorMath.ToHsv(new Rgb(c.R, c.G, c.B));
        var back = ColorMath.FromHsv(new Hsv(_h, _s, _v));
        if (back.R == c.R && back.G == c.G && back.B == c.B) return; // already there (no rounding drift)
        if (hsv.S > 0 && hsv.V > 0) _h = hsv.H;
        _s = hsv.S;
        _v = hsv.V;
    }

    private void UpdateVisuals()
    {
        var pure = ColorMath.FromHsv(new Hsv(_h, 1, 1));
        _hueFill.Fill = new SolidColorBrush(Color.FromRgb(pure.R, pure.G, pure.B));
        double w = Inner(_area, true), h = Inner(_area, false);
        Canvas.SetLeft(_areaKnob, _s * w - Knob / 2);
        Canvas.SetTop(_areaKnob, (1 - _v) * h - Knob / 2);
        double hh = Inner(_hue, false);
        Canvas.SetLeft(_hueKnob, -4);
        Canvas.SetTop(_hueKnob, _h / 360 * hh - 5);
    }
}
