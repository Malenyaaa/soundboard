using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Soundboard.Core.Hotkeys;

namespace Soundboard.App.Views;

/// <summary>
/// One line of text that picks the longest of several variants that fits the room it gets
/// (key caps: "Strg+Alt+Umschalt+F12" → "…Umschalt+F12" → "…F12"). Nothing is cut in the middle
/// of a word; the full text belongs in the tooltip.
/// </summary>
public sealed class FitText : FrameworkElement
{
    public static readonly DependencyProperty VariantsProperty = DependencyProperty.Register(
        nameof(Variants), typeof(IReadOnlyList<string>), typeof(FitText),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(FitText),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(FitText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(FitText),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(FitText),
        new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    private FormattedText? _text;

    public IReadOnlyList<string>? Variants
    {
        get => (IReadOnlyList<string>?)GetValue(VariantsProperty);
        set => SetValue(VariantsProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>The variant on screen (for tests and the preview).</summary>
    public string Shown { get; private set; } = "";

    private FormattedText Make(string s) => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal), FontSize, Foreground,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override Size MeasureOverride(Size available)
    {
        var variants = Variants ?? [];
        Shown = KeyCapText.Fit(variants, available.Width, s => Make(s).WidthIncludingTrailingWhitespace);
        _text = Make(Shown);
        return new Size(Math.Min(Math.Ceiling(_text.WidthIncludingTrailingWhitespace), available.Width), Math.Ceiling(_text.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_text is null) return;
        _text.SetForegroundBrush(Foreground);
        dc.DrawText(_text, new Point(0, 0));
    }
}
