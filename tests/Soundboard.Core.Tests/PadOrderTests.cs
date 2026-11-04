using Soundboard.Core.Config;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class PadOrderTests
{
    // Two rows of three pads, 100 × 80, gap 10: row 1 at y 0, row 2 at y 90; the last row holds two.
    private static readonly PadBox[] Grid =
    [
        new(0, 0, 100, 80), new(110, 0, 100, 80), new(220, 0, 100, 80),
        new(0, 90, 100, 80), new(110, 90, 100, 80),
    ];

    [TestMethod]
    public void Left_half_inserts_before_right_half_after()
    {
        Assert.AreEqual(1, PadOrder.InsertIndex(Grid, 120, 40));
        Assert.AreEqual(2, PadOrder.InsertIndex(Grid, 200, 40));
        Assert.AreEqual(3, PadOrder.InsertIndex(Grid, 300, 40), "right half of the last pad in a row: before the first pad of the next row");
        Assert.AreEqual(0, PadOrder.InsertIndex(Grid, -20, 40));
    }

    [TestMethod]
    public void Rows_are_found_by_height_and_the_gap_belongs_to_the_row_above()
    {
        Assert.AreEqual(4, PadOrder.InsertIndex(Grid, 120, 120));
        Assert.AreEqual(5, PadOrder.InsertIndex(Grid, 400, 150), "behind the last pad = at the end");
        Assert.AreEqual(1, PadOrder.InsertIndex(Grid, 120, 85), "in the gap under row 1");
        Assert.AreEqual(1, PadOrder.InsertIndex(Grid, 120, -30), "above everything = first row");
        Assert.AreEqual(5, PadOrder.InsertIndex(Grid, 300, 900), "far below = last row");
        Assert.AreEqual(0, PadOrder.InsertIndex([], 10, 10));
    }

    [TestMethod]
    public void Target_index_accounts_for_the_pad_taken_out()
    {
        Assert.AreEqual(2, PadOrder.TargetIndex(from: 0, insertBefore: 3));
        Assert.AreEqual(1, PadOrder.TargetIndex(from: 3, insertBefore: 1));
        Assert.AreEqual(2, PadOrder.TargetIndex(from: 2, insertBefore: 2), "before itself: stays");
        Assert.AreEqual(2, PadOrder.TargetIndex(from: 2, insertBefore: 3), "before its neighbour: stays");
    }

    [TestMethod]
    public void Move_reorders_and_clamps()
    {
        var list = new List<string> { "a", "b", "c", "d" };
        PadOrder.Move(list, 0, 2);
        CollectionAssert.AreEqual(new[] { "b", "c", "a", "d" }, list);
        PadOrder.Move(list, 3, -5);
        CollectionAssert.AreEqual(new[] { "d", "b", "c", "a" }, list);
        PadOrder.Move(list, 9, 0);
        CollectionAssert.AreEqual(new[] { "d", "b", "c", "a" }, list, "unknown index: nothing happens");
    }
}
