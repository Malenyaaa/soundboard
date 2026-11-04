using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Soundboard.App.Themes;
using Soundboard.Core.Audio;

namespace Soundboard.App.Views;

/// <summary>
/// Drawn speaker that shows a volume: struck through at 0 %, one to three sound arcs above.
/// Only a change of step moves anything (arcs fade, the stroke draws in); dragging inside one
/// step changes nothing. With animations off the steps switch at once.
/// Usage: <c>&lt;views:VolumeIcon Percent="{Binding …}" Stroke="{DynamicResource TextBrush}"
/// Text="…" MutedText="…" /&gt;</c>.
/// </summary>
public sealed class VolumeIcon : Grid
{
    private const double Thickness = 1.5;
    private static readonly Duration In = new(TimeSpan.FromMilliseconds(140));
    private static readonly Duration Out = new(TimeSpan.FromMilliseconds(100));
    private static readonly Duration Draw = new(TimeSpan.FromMilliseconds(160));
    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    // Slash length in units of the stroke thickness (√(14² + 15²) / 1.5 ≈ 13.7), rounded up.
    private const double SlashUnits = 14;

    private readonly Path _body;
    private readonly Path[] _arcs;
    private readonly Path _slash;
    private int _level = -1;

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(VolumeIcon), new PropertyMetadata(100.0, (d, _) => ((VolumeIcon)d).Update(animate: true)));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(VolumeIcon), new PropertyMetadata(Brushes.Black));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(VolumeIcon), new PropertyMetadata("", (d, _) => ((VolumeIcon)d).UpdateText()));

    public static readonly DependencyProperty MutedTextProperty = DependencyProperty.Register(
        nameof(MutedText), typeof(string), typeof(VolumeIcon), new PropertyMetadata("", (d, _) => ((VolumeIcon)d).UpdateText()));

    /// <summary>Volume in percent (0–100), as on the slider.</summary>
    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }

    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    /// <summary>Tooltip and name for screen readers while there is sound.</summary>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>Tooltip and name for screen readers at 0 %.</summary>
    public string MutedText { get => (string)GetValue(MutedTextProperty); set => SetValue(MutedTextProperty, value); }

    /// <summary>The step shown now (0 = muted, 1–3 arcs).</summary>
    public int Level => _level;

    public VolumeIcon()
    {
        Width = 22;
        Height = 20;
        SnapsToDevicePixels = true;
        Background = Brushes.Transparent; // the tooltip answers on the whole symbol, not only on its lines
        _body = Part("M 1,7 L 5,7 L 10,2.5 L 10,17.5 L 5,13 L 1,13 Z");
        _arcs =
        [
            Part("M 12.6,7.4 Q 15,10 12.6,12.6"),
            Part("M 15.2,5 Q 18.8,10 15.2,15"),
            Part("M 17.8,2.6 Q 22.6,10 17.8,17.4"),
        ];
        _slash = Part("M 0.5,2.5 L 14.5,17.5");
        _slash.StrokeDashArray = [SlashUnits, SlashUnits + 4];
        _slash.StrokeDashCap = PenLineCap.Round;
        Update(animate: false);
    }

    private Path Part(string data)
    {
        var p = new Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = Thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.None,
        };
        p.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(Stroke)) { Source = this });
        Children.Add(p);
        return p;
    }

    private void Update(bool animate)
    {
        int level = VolumeLevel.Of(Percent);
        if (level == _level) return;
        bool first = _level < 0;
        _level = level;
        bool move = animate && !first && IsLoaded && ThemeManager.AnimationsOn;

        for (int i = 0; i < _arcs.Length; i++)
            Fade(_arcs[i], level > i ? 1 : 0, move);

        bool muted = level == VolumeLevel.Muted;
        if (muted && move)
        {
            // The stroke draws in from its top end, quickly visible.
            _slash.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Out) { EasingFunction = EaseOut });
            _slash.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(SlashUnits + 1, 0, Draw) { EasingFunction = EaseOut });
        }
        else if (muted)
        {
            _slash.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            _slash.StrokeDashOffset = 0;
            Fade(_slash, 1, move: false);
        }
        else
        {
            Fade(_slash, 0, move);
        }
        UpdateText();
    }

    /// <summary>Animates from the value shown now (so a quick change back never jumps) or sets it at once.</summary>
    private static void Fade(UIElement e, double to, bool move)
    {
        if (!move)
        {
            e.BeginAnimation(OpacityProperty, null);
            e.Opacity = to;
            return;
        }
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(to, to > 0 ? In : Out) { EasingFunction = EaseOut });
    }

    private void UpdateText()
    {
        string text = _level == VolumeLevel.Muted ? MutedText : Text;
        ToolTip = string.IsNullOrEmpty(text) ? null : text;
        AutomationProperties.SetName(this, text);
    }

    private static IEasingFunction Frozen(CubicEase e)
    {
        e.Freeze();
        return e;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(VolumeIcon owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetClassNameCore() => nameof(VolumeIcon);
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => false;
    }
}
