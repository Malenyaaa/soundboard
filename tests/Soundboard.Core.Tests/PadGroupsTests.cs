using Soundboard.Core.Config;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class PadGroupsTests
{
    private static (Profile Profile, CategoryConfig A, CategoryConfig B) Board()
    {
        var p = new Profile();
        var a = new CategoryConfig { Name = "Memes" };
        var b = new CategoryConfig { Name = "Musik" };
        p.Categories.AddRange([a, b]);
        return (p, a, b);
    }

    [TestMethod]
    public void Groups_are_favourites_then_categories_in_order_then_the_others()
    {
        var (p, a, b) = Board();
        var t = new[]
        {
            new TileConfig { Name = "o1" },
            new TileConfig { Name = "b1", Category = b.Id },
            new TileConfig { Name = "f1", Favorite = true, Category = b.Id },
            new TileConfig { Name = "a1", Category = a.Id },
            new TileConfig { Name = "o2", Category = Guid.NewGuid() }, // a category that is gone
            new TileConfig { Name = "a2", Category = a.Id },
        };
        var sorted = PadGroups.Sorted(t, x => x, p);
        CollectionAssert.AreEqual(new[] { "f1", "a1", "a2", "b1", "o1", "o2" }, sorted.Select(x => x.Name).ToArray());
        Assert.AreEqual(GroupKey.Favorites, PadGroups.Of(t[2], p), "a favourite is in the favourites, whatever its category");
        Assert.AreEqual(GroupKey.Others, PadGroups.Of(t[4], p));
    }

    [TestMethod]
    public void A_favourite_goes_back_to_its_category()
    {
        var (_, a, _) = Board();
        var tile = new TileConfig { Category = a.Id };
        PadGroups.Put(tile, GroupKey.Favorites);
        Assert.IsTrue(tile.Favorite);
        Assert.AreEqual(a.Id, tile.Category, "the category is kept");
        PadGroups.Put(tile, GroupKey.Others);
        Assert.IsFalse(tile.Favorite);
        Assert.IsNull(tile.Category, "dragged to the other pads: in no category");
    }

    [TestMethod]
    public void New_names_are_numbered()
    {
        Assert.AreEqual("Kategorie", PadGroups.FreeName("Kategorie", ["Memes"]));
        Assert.AreEqual("Kategorie 3", PadGroups.FreeName("Kategorie", ["kategorie", "Kategorie 2"]));
    }

    [TestMethod]
    public void Migrate_drops_doubles_and_lost_links_and_names_empty_categories()
    {
        var id = Guid.NewGuid();
        var p = new Profile
        {
            Categories = [new CategoryConfig { Id = id, Name = " " }, new CategoryConfig { Id = id, Name = "double" }],
            Tiles = [new TileConfig { Color = "#3A78D4", Category = id }, new TileConfig { Color = "#3A78D4", Category = Guid.NewGuid() }],
        };
        Assert.IsTrue(p.Migrate());
        Assert.AreEqual(1, p.Categories.Count);
        Assert.IsFalse(string.IsNullOrWhiteSpace(p.Categories[0].Name));
        Assert.AreEqual(id, p.Tiles[0].Category);
        Assert.IsNull(p.Tiles[1].Category);
    }

    // ---- drop place: two groups of pads 100 × 80 in rows of three, heads 28 high

    private static readonly GroupKey A = GroupKey.Category(Guid.NewGuid()), B = GroupKey.Category(Guid.NewGuid());

    private static GroupedBox Pad(double x, double y, GroupKey g) => new(new PadBox(x, y, 100, 80), g);

    private static HeadBox Head(double y, GroupKey g) => new(new PadBox(0, y, 330, 28), g);

    // A: head at 0, pads at y 36 (3) and 126 (1); B: head at 222, pads at y 258 (2)
    private static readonly GroupedBox[] Pads =
    [
        Pad(0, 36, A), Pad(110, 36, A), Pad(220, 36, A),
        Pad(0, 126, A),
        Pad(0, 258, B), Pad(110, 258, B),
    ];

    private static readonly HeadBox[] Heads = [Head(0, A), Head(222, B)];

    [TestMethod]
    public void Left_half_before_right_half_after()
    {
        Assert.AreEqual(new DropPlace(A, 1), PadDrop.Find(Pads, Heads, 140, 60));
        Assert.AreEqual(new DropPlace(A, 2), PadDrop.Find(Pads, Heads, 170, 60));
    }

    [TestMethod]
    public void Past_the_last_pad_of_a_group_stays_in_that_group()
    {
        // Right of the single pad in A's second row: the end of A, not the start of B.
        Assert.AreEqual(new DropPlace(A, 4), PadDrop.Find(Pads, Heads, 300, 150));
        Assert.AreEqual(new DropPlace(B, 6), PadDrop.Find(Pads, Heads, 300, 280));
    }

    [TestMethod]
    public void On_a_head_the_pad_goes_to_the_end_of_that_group()
    {
        Assert.AreEqual(new DropPlace(B, 6), PadDrop.Find(Pads, Heads, 10, 230));
        Assert.AreEqual(new DropPlace(A, 4), PadDrop.Find(Pads, Heads, 10, 5));
    }

    [TestMethod]
    public void Between_a_head_and_its_first_row_counts_as_that_row()
    {
        // y 252: below B's head (ends at 250), above B's first row (258).
        Assert.AreEqual(new DropPlace(B, 4), PadDrop.Find(Pads, Heads, 10, 252));
    }

    [TestMethod]
    public void An_empty_group_takes_the_pad_on_its_head_and_below_it()
    {
        var c = GroupKey.Category(Guid.NewGuid());
        // C is empty: its head at 222 is followed by B's head at 258 (B moved down).
        var pads = Pads.Take(4).Concat([Pad(0, 294, B)]).ToArray();
        var heads = new[] { Head(0, A), Head(222, c), Head(258, B) };
        Assert.AreEqual(new DropPlace(c, 4), PadDrop.Find(pads, heads, 50, 230));
        Assert.AreEqual(new DropPlace(c, 4), PadDrop.Find(pads, heads, 50, 252), "the gap below an empty head");
    }

    [TestMethod]
    public void Nothing_to_drop_on()
    {
        Assert.IsNull(PadDrop.Find([], [], 10, 10));
    }

    [TestMethod]
    public void Dragged_category_goes_after_every_group_whose_middle_is_above_the_pointer()
    {
        // three other categories: 0–100, 100–400 (many pads), 400–450 (empty)
        var spans = new List<(double, double)> { (0, 100), (100, 400), (400, 450) };
        Assert.AreEqual(0, PadDrop.CategoryIndex(spans, -20));
        Assert.AreEqual(0, PadDrop.CategoryIndex(spans, 40));
        Assert.AreEqual(1, PadDrop.CategoryIndex(spans, 60));
        Assert.AreEqual(1, PadDrop.CategoryIndex(spans, 240));
        Assert.AreEqual(2, PadDrop.CategoryIndex(spans, 260));
        Assert.AreEqual(3, PadDrop.CategoryIndex(spans, 900));
        Assert.AreEqual(0, PadDrop.CategoryIndex([], 50));
    }
}
