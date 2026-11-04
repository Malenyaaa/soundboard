using System.Windows;
using System.Windows.Controls;

namespace Soundboard.App.Views;

/// <summary>
/// Top row of a pad: the key cap on the left, the length on the right. When both do not fit,
/// the length gives way first; only then the cap gets less room (and shortens itself from the
/// front). The first child is the cap area, the last one the length.
/// </summary>
public sealed class PadTopRow : Panel
{
    private bool _showLast = true;

    public PadTopRow() => ClipToBounds = true;

    protected override Size MeasureOverride(Size available)
    {
        if (InternalChildren.Count == 0) return default;
        var first = InternalChildren[0];
        var last = InternalChildren.Count > 1 ? InternalChildren[InternalChildren.Count - 1] : null;
        var free = new Size(double.PositiveInfinity, available.Height);
        first.Measure(free);
        last?.Measure(free);
        double lastWidth = last?.DesiredSize.Width ?? 0;
        _showLast = last is not null && first.DesiredSize.Width + lastWidth <= available.Width;
        if (!_showLast) first.Measure(new Size(available.Width, available.Height));
        double height = Math.Max(first.DesiredSize.Height, _showLast ? last!.DesiredSize.Height : 0);
        double width = double.IsInfinity(available.Width) ? first.DesiredSize.Width + lastWidth : available.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (InternalChildren.Count == 0) return final;
        var first = InternalChildren[0];
        first.Arrange(new Rect(0, 0, Math.Min(first.DesiredSize.Width, final.Width), first.DesiredSize.Height));
        if (InternalChildren.Count > 1)
        {
            var last = InternalChildren[InternalChildren.Count - 1];
            // Hidden: laid out outside the clip, so it is neither seen nor hit.
            double x = _showLast ? final.Width - last.DesiredSize.Width : final.Width + 10000;
            last.Arrange(new Rect(x, 0, last.DesiredSize.Width, last.DesiredSize.Height));
        }
        return final;
    }
}
