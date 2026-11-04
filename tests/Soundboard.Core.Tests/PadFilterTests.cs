using Soundboard.Core.Config;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class PadFilterTests
{
    [TestMethod]
    public void Empty_search_shows_everything()
    {
        Assert.IsTrue(PadFilter.Matches("Airhorn", null, ""));
        Assert.IsTrue(PadFilter.Matches("Airhorn", null, "   "));
        Assert.IsTrue(PadFilter.Matches("Airhorn", null, null));
        Assert.IsFalse(PadFilter.IsActive(" "));
    }

    [TestMethod]
    public void Words_are_found_in_name_or_key_ignoring_case_and_accents()
    {
        Assert.IsTrue(PadFilter.Matches("Airhorn", "Num 1", "air"));
        Assert.IsTrue(PadFilter.Matches("Airhorn", "Num 1", "NUM 1"));
        Assert.IsTrue(PadFilter.Matches("Grillen zirpen", "Strg+Alt+3", "strg grill"), "one word in the key, one in the name");
        Assert.IsTrue(PadFilter.Matches("Café-Musik", null, "cafe"));
        Assert.IsTrue(PadFilter.Matches("Trommelwirbel", null, "wirbel"));
    }

    [TestMethod]
    public void Every_word_must_match()
    {
        Assert.IsFalse(PadFilter.Matches("Airhorn", "Num 1", "air drum"));
        Assert.IsFalse(PadFilter.Matches("Airhorn", null, "num"), "no key, so no match on the key");
        Assert.IsFalse(PadFilter.Matches(null, null, "x"));
    }
}
