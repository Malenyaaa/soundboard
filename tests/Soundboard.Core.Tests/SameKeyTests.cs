using Soundboard.Core.Config;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class SameKeyTests
{
    [TestMethod]
    public void Default_is_stop_and_unknown_values_fall_back_to_it()
    {
        Assert.AreEqual(SameKeyModes.Stop, new Profile().SameKeyAgain);
        var p = new Profile { SameKeyAgain = "Bounce" };
        Assert.IsTrue(p.Migrate());
        Assert.AreEqual(SameKeyModes.Stop, p.SameKeyAgain);
        var r = new Profile { SameKeyAgain = SameKeyModes.Restart };
        r.Migrate();
        Assert.AreEqual(SameKeyModes.Restart, r.SameKeyAgain);
    }
}
