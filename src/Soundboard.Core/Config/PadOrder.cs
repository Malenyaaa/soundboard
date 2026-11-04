namespace Soundboard.Core.Config;

/// <summary>A pad's box on screen, for finding where a dragged pad lands.</summary>
public readonly record struct PadBox(double X, double Y, double Width, double Height)
{
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
}

/// <summary>Reordering pads: by dragging (insert mark between pads) and by keyboard (one step).</summary>
public static class PadOrder
{
    /// <summary>
    /// Where a pad dropped at (<paramref name="x"/>, <paramref name="y"/>) goes: the index of the pad
    /// it is inserted before, or <c>boxes.Count</c> for "at the end". The row is the one under the
    /// pointer (above the first row counts as the first, below the last as the last); in the row the
    /// left half of a pad means "before it", the right half "after it".
    /// </summary>
    public static int InsertIndex(IReadOnlyList<PadBox> boxes, double x, double y)
    {
        if (boxes.Count == 0) return 0;
        var rowTops = boxes.Select(b => b.Y).Distinct().Order().ToList();
        double rowTop = rowTops[0];
        foreach (var top in rowTops)
        {
            if (y >= top) rowTop = top;
        }
        var row = Enumerable.Range(0, boxes.Count).Where(i => boxes[i].Y == rowTop).OrderBy(i => boxes[i].X).ToList();
        foreach (int i in row)
            if (x < boxes[i].CenterX) return i;
        return row[^1] + 1;
    }

    /// <summary>
    /// Index the pad at <paramref name="from"/> takes when it is inserted before <paramref name="insertBefore"/>
    /// (an index in the order before the move). Equal to <paramref name="from"/> when nothing moves.
    /// </summary>
    public static int TargetIndex(int from, int insertBefore) => insertBefore > from ? insertBefore - 1 : insertBefore;

    /// <summary>Moves one item; indices outside the list are clamped.</summary>
    public static void Move<T>(IList<T> list, int from, int to)
    {
        if (from < 0 || from >= list.Count) return;
        to = Math.Clamp(to, 0, list.Count - 1);
        if (to == from) return;
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
    }
}
