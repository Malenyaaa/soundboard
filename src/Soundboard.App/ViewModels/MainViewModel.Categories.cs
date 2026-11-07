using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.Config;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>
/// Groups of pads: the favourites, own categories and the pads in none ("Other pads"), each under a head
/// that can be collapsed. The pad grid shows <see cref="PadItems"/>: every head followed by the pads of its
/// group. <see cref="Tiles"/> (and the profile) always keep the groups together, in the order of the heads.
/// Heads show only once there is an own category or a favourite; without them the grid is as before.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>What the pad grid shows: heads (<see cref="CategoryViewModel"/>) and pads (<see cref="TileViewModel"/>).</summary>
    public ObservableCollection<object> PadItems { get; } = [];

    private readonly Dictionary<GroupKey, CategoryViewModel> _heads = [];

    /// <summary>A category was just made: the window brings its head into view (its name is open for typing).</summary>
    public event Action<CategoryViewModel>? CategoryAdded;

    /// <summary>The groups or what they show changed (the grid lays the heads out again).</summary>
    public event Action? GroupsChanged;

    public string? FavoritesName => _profile.FavoritesName;

    public int OwnCategoryCount => _profile.Categories.Count;

    public int CategoryIndex(CategoryViewModel head) => head.Config is null ? -1 : _profile.Categories.IndexOf(head.Config);

    public GroupKey GroupOf(TileViewModel tile) => PadGroups.Of(tile.Config, _profile);

    /// <summary>Heads are shown once there is an own category or a favourite.</summary>
    public bool HeadsShown => _profile.Categories.Count > 0 || Tiles.Any(t => t.Config.Favorite);

    public bool IsGroupCollapsed(GroupKey group) => group.Kind switch
    {
        GroupKind.Favorites => _profile.FavoritesCollapsed,
        GroupKind.Category => _profile.Categories.FirstOrDefault(c => c.Id == group.Id)?.Collapsed ?? false,
        _ => _profile.OthersCollapsed,
    };

    /// <summary>The head of a group (made when first needed).</summary>
    public CategoryViewModel Head(GroupKey group)
    {
        if (_heads.TryGetValue(group, out var head)) return head;
        head = new CategoryViewModel(this, group, group.Kind == GroupKind.Category ? _profile.Categories.First(c => c.Id == group.Id) : null);
        _heads[group] = head;
        return head;
    }

    /// <summary>
    /// Brings heads, counts, collapsed pads and <see cref="PadItems"/> up to date. Called whenever pads, groups or the
    /// search change. Containers are kept (moved, not made new), so the grid can let pads glide to their new places.
    /// </summary>
    private void RefreshGroups()
    {
        var order = PadGroups.Order(_profile);
        foreach (var gone in _heads.Keys.Where(k => !order.Contains(k)).ToList()) _heads.Remove(gone);
        bool heads = HeadsShown;
        var byGroup = order.ToDictionary(g => g, _ => new List<TileViewModel>());
        foreach (var t in Tiles)
        {
            var g = GroupOf(t);
            byGroup[g].Add(t);
            if (t.IsFavorite != t.Config.Favorite) t.IsFavorite = t.Config.Favorite;
        }
        var desired = new List<object>(Tiles.Count + order.Count);
        foreach (var g in order)
        {
            var head = Head(g);
            var pads = byGroup[g];
            head.Count = pads.Count;
            // While searching, collapsed groups open for their matches; a head whose group has no match goes away.
            bool collapsed = heads && head.IsCollapsed && !SearchActive;
            head.IsHidden = !heads
                || (g.Kind == GroupKind.Favorites && pads.Count == 0)
                || (SearchActive && pads.All(p => p.IsFilteredOut));
            foreach (var p in pads) p.IsCollapsedOut = collapsed;
            desired.Add(head);
            desired.AddRange(pads);
        }
        SyncItems(PadItems, desired);
        foreach (var h in _heads.Values) h.RefreshTexts();
        GroupsChanged?.Invoke();
    }

    /// <summary>Makes <paramref name="target"/> equal to <paramref name="desired"/> with moves, inserts and removals (no reset).</summary>
    internal static void SyncItems(ObservableCollection<object> target, IReadOnlyList<object> desired)
    {
        var wanted = new HashSet<object>(desired, ReferenceEqualityComparer.Instance);
        for (int i = target.Count - 1; i >= 0; i--)
            if (!wanted.Contains(target[i])) target.RemoveAt(i);
        for (int i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i])) continue;
            int at = -1;
            for (int j = i + 1; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], desired[i]))
                {
                    at = j;
                    break;
                }
            }
            if (at >= 0) target.Move(at, i);
            else target.Insert(i, desired[i]);
        }
    }

    /// <summary>Puts the groups together in the order of the heads, keeping the order inside each group.</summary>
    private void KeepGroupsTogether()
    {
        var sorted = PadGroups.Sorted(Tiles, t => t.Config, _profile);
        for (int i = 0; i < sorted.Count; i++)
            if (!ReferenceEquals(Tiles[i], sorted[i])) MoveTileRaw(sorted[i], i);
    }

    /// <summary>Allowed place for <paramref name="tile"/> (an index in the order without it): inside its own group.</summary>
    private int ClampToGroup(TileViewModel tile, int index)
    {
        var (start, end) = GroupRange(GroupOf(tile), tile);
        return Math.Clamp(index, start, end);
    }

    /// <summary>
    /// Where a group's pads are in <see cref="Tiles"/> without <paramref name="leaving"/>: the first index and the index
    /// just after its last pad (equal when the group is empty: where its pads would start).
    /// </summary>
    private (int Start, int End) GroupRange(GroupKey group, TileViewModel? leaving)
    {
        int rank = PadGroups.Rank(group, _profile), start = 0, count = 0;
        foreach (var t in Tiles)
        {
            if (ReferenceEquals(t, leaving)) continue;
            int r = PadGroups.Rank(GroupOf(t), _profile);
            if (r < rank) start++;
            else if (r == rank) count++;
        }
        return (start, start + count);
    }

    /// <summary>
    /// Moves a pad into <paramref name="group"/>, in front of <paramref name="before"/> when that pad is in the same group,
    /// else to the end of the group. Remembered for Ctrl+Z with <paramref name="undoText"/> (null: not remembered).
    /// </summary>
    public void MoveTileTo(TileViewModel tile, GroupKey group, TileViewModel? before, Func<string>? undoText)
    {
        int from = Tiles.IndexOf(tile);
        if (from < 0) return;
        bool wasFavorite = tile.Config.Favorite;
        var wasCategory = tile.Config.Category;
        PadGroups.Put(tile.Config, group);
        tile.IsFavorite = tile.Config.Favorite;
        int to = before is not null && !ReferenceEquals(before, tile) && GroupOf(before) == group
            ? Tiles.Where(t => !ReferenceEquals(t, tile)).ToList().IndexOf(before)
            : GroupRange(group, tile).End;
        if (to != from) MoveTileRaw(tile, to);
        else ScheduleSave();
        RefreshGroups();
        if (undoText is null) return;
        var config = tile.Config;
        PushUndo(undoText, () =>
        {
            if (Tiles.FirstOrDefault(t => ReferenceEquals(t.Config, config)) is not { } now) return;
            now.Config.Favorite = wasFavorite;
            now.Config.Category = wasCategory;
            now.IsFavorite = wasFavorite;
            MoveTileRaw(now, Math.Clamp(from, 0, Tiles.Count - 1));
            KeepGroupsTogether();
            RefreshGroups();
        });
    }

    /// <summary>Drop after dragging a pad: into <paramref name="group"/>, in front of <paramref name="before"/> (null = at the end of the group).</summary>
    public void DropTile(TileViewModel tile, GroupKey group, TileViewModel? before)
    {
        if (GroupOf(tile) == group)
        {
            // Inside its group: the plain move (also remembered).
            if (before is null || GroupOf(before) != group) MoveTile(tile, GroupRange(group, tile).End);
            else MoveTileBefore(tile, before);
            return;
        }
        var name = tile.Name;
        string groupName = Head(group).Name;
        MoveTileTo(tile, group, before, () => L.F("Notice.PadMovedTo", name, groupName));
    }

    // ---------------------------------------------------------------- categories

    /// <summary>
    /// A new category at the end of the own categories, its name open for typing. With <paramref name="tile"/> that
    /// pad goes into it (right-click → "Move to" → "New category").
    /// </summary>
    public void AddCategory(TileViewModel? tile)
    {
        var config = new CategoryConfig
        {
            Name = PadGroups.FreeName(L.Get("Category.NewName"), _profile.Categories.Select(c => c.Name)),
        };
        _profile.Categories.Add(config);
        var group = GroupKey.Category(config.Id);
        (TileViewModel Tile, bool Favorite, Guid? Category, int Index)? moved =
            tile is null ? null : (tile, tile.Config.Favorite, tile.Config.Category, Tiles.IndexOf(tile));
        if (tile is not null) MoveTileTo(tile, group, null, undoText: null);
        else RefreshGroups();
        RefreshMoveMenus();
        ScheduleSave();
        var head = Head(group);
        head.IsRenaming = true;
        CategoryAdded?.Invoke(head);
        PushUndo(() => L.F("Notice.CategoryAdded", config.Name), () =>
        {
            if (moved is { } m && Tiles.FirstOrDefault(t => ReferenceEquals(t.Config, m.Tile.Config)) is { } now)
            {
                now.Config.Favorite = m.Favorite;
                now.Config.Category = m.Category;
                MoveTileRaw(now, Math.Clamp(m.Index, 0, Tiles.Count - 1));
            }
            _profile.Categories.Remove(config);
            foreach (var t in Tiles.Where(t => t.Config.Category == config.Id)) t.Config.Category = null;
            KeepGroupsTogether();
            RefreshGroups();
            RefreshMoveMenus();
            ScheduleSave();
        });
    }

    /// <summary>Right-click on a pad → "Move to" → "New category …".</summary>
    [RelayCommand]
    private void NewCategory() => AddCategory(null);

    /// <summary>A name typed in a head; empty keeps the old one (the rename box sees to that).</summary>
    public void RenameGroup(CategoryViewModel head, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        if (head.Group.Kind == GroupKind.Favorites)
            _profile.FavoritesName = name == L.Get("Pads.Favorites") ? null : name;
        else if (head.Config is { } c)
            c.Name = name;
        else
            return;
        RefreshMoveMenus();
        ScheduleSave();
    }

    public void SetGroupCollapsed(CategoryViewModel head, bool collapsed)
    {
        switch (head.Group.Kind)
        {
            case GroupKind.Favorites: _profile.FavoritesCollapsed = collapsed; break;
            case GroupKind.Category when head.Config is { } c: c.Collapsed = collapsed; break;
            case GroupKind.Others: _profile.OthersCollapsed = collapsed; break;
        }
        ScheduleSave();
        RefreshGroups();
    }

    /// <summary>Deletes an own category; its pads go to "Other pads" (favourites stay favourites). Ctrl+Z brings it back.</summary>
    public void DeleteGroup(CategoryViewModel head)
    {
        if (head.Config is not { } config) return;
        int index = _profile.Categories.IndexOf(config);
        if (index < 0) return;
        var members = Tiles.Where(t => t.Config.Category == config.Id).Select(t => t.Config).ToList();
        foreach (var c in members) c.Category = null;
        _profile.Categories.RemoveAt(index);
        KeepGroupsTogether();
        RefreshGroups();
        RefreshMoveMenus();
        ScheduleSave();
        PushUndo(() => L.F("Notice.CategoryDeleted", config.Name), () =>
        {
            if (_profile.Categories.Contains(config)) return;
            _profile.Categories.Insert(Math.Clamp(index, 0, _profile.Categories.Count), config);
            foreach (var c in members) c.Category = config.Id;
            KeepGroupsTogether();
            RefreshGroups();
            RefreshMoveMenus();
            ScheduleSave();
        });
    }

    /// <summary>Moves an own category one place up (−1) or down (+1) among the own categories.</summary>
    public void MoveCategory(CategoryViewModel head, int delta)
    {
        if (head.Config is not { } config) return;
        int from = _profile.Categories.IndexOf(config), to = from + delta;
        if (from < 0 || to < 0 || to >= _profile.Categories.Count) return;
        PadOrder.Move(_profile.Categories, from, to);
        KeepGroupsTogether();
        RefreshGroups();
        RefreshMoveMenus();
        ScheduleSave();
        PushUndo(() => L.F("Notice.CategoryMoved", config.Name), () =>
        {
            int now = _profile.Categories.IndexOf(config);
            if (now < 0) return;
            PadOrder.Move(_profile.Categories, now, from);
            KeepGroupsTogether();
            RefreshGroups();
            RefreshMoveMenus();
            ScheduleSave();
        });
    }

    /// <summary>The "Move to" submenus of the pads list the groups: after a category came, went, moved or was renamed.</summary>
    private void RefreshMoveMenus()
    {
        foreach (var t in Tiles) t.RefreshMoveMenu();
    }

    /// <summary>Targets of a pad's "Move to" submenu, in the order of the groups.</summary>
    public IReadOnlyList<(string Name, GroupKey Group)> MoveTargets() =>
        PadGroups.Order(_profile).Select(g => (Head(g).Name, g)).ToList();
}
