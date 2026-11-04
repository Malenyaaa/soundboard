using Soundboard.Core.Config;
using Soundboard.Core.Hotkeys;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class HotkeyRulesTests
{
    private static readonly HotkeyGesture Pause = HotkeyGesture.StopAllDefault;
    private static readonly HotkeyGesture Num1 = new(HotkeyModifiers.None, 0x61);
    private static readonly HotkeyGesture F8 = new(HotkeyModifiers.None, 0x77);

    private static bool IsFunction(string owner) => owner is "stop-all" or "mute-mic";

    private static (string, HotkeyGesture?)[] Keys(HotkeyGesture? stop, HotkeyGesture? mute = null) =>
    [
        ("stop-all", stop),
        ("mute-mic", mute),
        ("pad-a", Num1),
        ("pad-b", null),
    ];

    [TestMethod]
    public void A_pad_cannot_take_the_stop_key()
    {
        Assert.AreEqual("stop-all", HotkeyRules.Conflict(Pause, "pad-b", Keys(Pause), IsFunction));
    }

    [TestMethod]
    public void Two_pads_may_share_a_key_for_a_moment()
    {
        // The second pad shows "duplicate" with "move here"; that is no refusal.
        Assert.IsNull(HotkeyRules.Conflict(Num1, "pad-b", Keys(Pause), IsFunction));
    }

    [TestMethod]
    public void The_stop_key_cannot_take_a_pad_key_or_the_mute_key()
    {
        Assert.AreEqual("pad-a", HotkeyRules.Conflict(Num1, "stop-all", Keys(Pause), IsFunction));
        Assert.AreEqual("mute-mic", HotkeyRules.Conflict(F8, "stop-all", Keys(Pause, F8), IsFunction));
        Assert.AreEqual("stop-all", HotkeyRules.Conflict(Pause, "mute-mic", Keys(Pause, F8), IsFunction));
    }

    [TestMethod]
    public void A_free_key_and_the_own_key_are_fine()
    {
        Assert.IsNull(HotkeyRules.Conflict(F8, "stop-all", Keys(Pause), IsFunction));
        Assert.IsNull(HotkeyRules.Conflict(Pause, "stop-all", Keys(Pause), IsFunction));
        // Without a stop key, Pause is free for a pad.
        Assert.IsNull(HotkeyRules.Conflict(Pause, "pad-b", Keys(null), IsFunction));
    }

    [TestMethod]
    public void No_stop_key_survives_saving_and_loading()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sb-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(dir, "config.json");
            Assert.AreEqual("Pause", new Profile().StopAllHotkey, "default stays Pause");
            ProfileStore.Save(file, new Profile { StopAllHotkey = null });
            Assert.IsNull(ProfileStore.Load(file).Profile.StopAllHotkey);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
