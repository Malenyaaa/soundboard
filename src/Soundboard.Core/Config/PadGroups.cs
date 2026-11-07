namespace Soundboard.Core.Config;

public enum GroupKind
{
    Favorites,
    Category,
    Others,
}

/// <summary>A group of pads on screen: the favourites, one own category, or the pads in none ("Other pads").</summary>
public readonly record struct GroupKey(GroupKind Kind, Guid Id)
{
    public static readonly GroupKey Favorites = new(GroupKind.Favorites, Guid.Empty);
    public static readonly GroupKey Others = new(GroupKind.Others, Guid.Empty);

    public static GroupKey Category(Guid id) => new(GroupKind.Category, id);
}

/// <summary>
/// The pads in groups: favourites first, then the own categories in their order, then the others. The
/// order of the pads inside a group is their order in the profile; the profile keeps the groups together.
/// </summary>
public static class PadGroups
{
    /// <summary>The group a pad is in. A favourite is in the favourites, whatever its category.</summary>
    public static GroupKey Of(TileConfig tile, Profile profile) =>
        tile.Favorite ? GroupKey.Favorites
        : tile.Category is { } id && profile.Categories.Any(c => c.Id == id) ? GroupKey.Category(id)
        : GroupKey.Others;

    /// <summary>All groups in the order they are shown.</summary>
    public static List<GroupKey> Order(Profile profile) =>
        [GroupKey.Favorites, .. profile.Categories.Select(c => GroupKey.Category(c.Id)), GroupKey.Others];

    /// <summary>Rank of a group in <see cref="Order"/>.</summary>
    public static int Rank(GroupKey group, Profile profile) => group.Kind switch
    {
        GroupKind.Favorites => 0,
        GroupKind.Category => 1 + profile.Categories.FindIndex(c => c.Id == group.Id),
        _ => 1 + profile.Categories.Count,
    };

    /// <summary>
    /// <paramref name="items"/> sorted by group, keeping the order inside each group (a stable sort).
    /// </summary>
    public static List<T> Sorted<T>(IEnumerable<T> items, Func<T, TileConfig> config, Profile profile) =>
        items.Select((item, i) => (item, i, rank: Rank(Of(config(item), profile), profile)))
            .OrderBy(x => x.rank).ThenBy(x => x.i).Select(x => x.item).ToList();

    /// <summary>
    /// Puts a pad into a group. Into the favourites it keeps its category (it goes back there when it stops
    /// being a favourite); into a category or "Other pads" it stops being a favourite.
    /// </summary>
    public static void Put(TileConfig tile, GroupKey group)
    {
        switch (group.Kind)
        {
            case GroupKind.Favorites:
                tile.Favorite = true;
                break;
            case GroupKind.Category:
                tile.Favorite = false;
                tile.Category = group.Id;
                break;
            default:
                tile.Favorite = false;
                tile.Category = null;
                break;
        }
    }

    /// <summary>
    /// A new category name that is not taken yet: <paramref name="baseName"/>, else "<paramref name="baseName"/> 2", 3 …
    /// </summary>
    public static string FreeName(string baseName, IEnumerable<string> taken)
    {
        var set = new HashSet<string>(taken, StringComparer.CurrentCultureIgnoreCase);
        if (!set.Contains(baseName)) return baseName;
        for (int n = 2; ; n++)
            if (!set.Contains($"{baseName} {n}")) return $"{baseName} {n}";
    }
}

/// <summary>Where a dragged pad lands: in front of a pad or at the end of a group.</summary>
public readonly record struct DropPlace(GroupKey Group, int Before);

/// <summary>A pad's box on screen in the drag preview, with its group.</summary>
public readonly record struct GroupedBox(PadBox Box, GroupKey Group);

/// <summary>A group head's box on screen (dropping on it puts the pad at the end of that group).</summary>
public readonly record struct HeadBox(PadBox Box, GroupKey Group);

public static class PadDrop
{
    /// <summary>
    /// Where a pad dragged with the pointer at (<paramref name="x"/>, <paramref name="y"/>) lands. <paramref name="pads"/>
    /// are the other shown pads in order (the dragged one left out); the result's <see cref="DropPlace.Before"/> is
    /// an index into them (<c>pads.Count</c> = after all). Over a head: the end of that group. Otherwise the row under
    /// the pointer (above the first row of a group counts as that row, below the last as the last): the left half of a
    /// pad means before it, past the middle of the last pad of the row means after it, in that pad's group.
    /// Null when there is nothing to drop on.
    /// </summary>
    public static DropPlace? Find(IReadOnlyList<GroupedBox> pads, IReadOnlyList<HeadBox> heads, double x, double y)
    {
        foreach (var h in heads)
        {
            if (y >= h.Box.Y && y < h.Box.Bottom)
            {
                // The end of the group: after its last pad (in order), else where its pads would start.
                int last = -1;
                for (int i = 0; i < pads.Count; i++) if (pads[i].Group == h.Group) last = i;
                if (last >= 0) return new DropPlace(h.Group, last + 1);
                int next = pads.Count;
                for (int i = 0; i < pads.Count; i++)
                {
                    if (pads[i].Box.Y > h.Box.Y)
                    {
                        next = i;
                        break;
                    }
                }
                return new DropPlace(h.Group, next);
            }
        }
        if (pads.Count == 0) return null;

        // The row: the lowest row top at or above the pointer; above everything the first row.
        double rowTop = pads.Min(p => p.Box.Y);
        foreach (var p in pads)
            if (p.Box.Y <= y && p.Box.Y > rowTop) rowTop = p.Box.Y;
        // Below a head but above the first row of its group: that head's group starts here, take its first row.
        var headAbove = heads.Where(h => h.Box.Bottom <= y).OrderByDescending(h => h.Box.Y).Cast<HeadBox?>().FirstOrDefault();
        if (headAbove is { } ha && ha.Box.Y > rowTop)
        {
            var firstOfGroup = pads.Where(p => p.Group == ha.Group).Select(p => p.Box.Y).DefaultIfEmpty(double.NaN).Min();
            if (double.IsNaN(firstOfGroup))
            {
                // The group is empty (only its head): the pad goes into it.
                int next = pads.Count;
                for (int i = 0; i < pads.Count; i++)
                {
                    if (pads[i].Box.Y > ha.Box.Y)
                    {
                        next = i;
                        break;
                    }
                }
                return new DropPlace(ha.Group, next);
            }
            rowTop = firstOfGroup;
        }

        var row = Enumerable.Range(0, pads.Count).Where(i => pads[i].Box.Y == rowTop).OrderBy(i => pads[i].Box.X).ToList();
        foreach (int i in row)
            if (x < pads[i].Box.CenterX) return new DropPlace(pads[i].Group, i);
        int lastInRow = row[^1];
        return new DropPlace(pads[lastInRow].Group, lastInRow + 1);
    }
}
