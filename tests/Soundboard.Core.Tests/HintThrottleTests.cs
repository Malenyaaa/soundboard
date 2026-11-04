using Soundboard.Core.Text;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class HintThrottleTests
{
    private static readonly DateTime T0 = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void Each_reason_speaks_at_most_once_per_ten_seconds()
    {
        var t = new HintThrottle(TimeSpan.FromSeconds(10));
        Assert.IsTrue(t.ShouldShow("loading", T0));
        Assert.IsFalse(t.ShouldShow("loading", T0.AddSeconds(3)));
        Assert.IsFalse(t.ShouldShow("loading", T0.AddSeconds(9.9)));
        Assert.IsTrue(t.ShouldShow("missing", T0.AddSeconds(4)), "another reason is not held back");
        Assert.IsTrue(t.ShouldShow("loading", T0.AddSeconds(10)));
        Assert.IsFalse(t.ShouldShow("loading", T0.AddSeconds(12)), "the interval starts again with every message shown");
    }

    [TestMethod]
    public void A_clock_that_jumps_back_does_not_silence_forever()
    {
        var t = new HintThrottle(TimeSpan.FromSeconds(10));
        Assert.IsTrue(t.ShouldShow("nowhere", T0));
        Assert.IsTrue(t.ShouldShow("nowhere", T0.AddMinutes(-5)));
    }
}
