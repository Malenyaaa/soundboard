using Soundboard.Core.Hotkeys;
using Soundboard.Core.Text;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class HotkeySuggestionTests
{
    [TestCleanup]
    public void BackToGerman() => L.SetLanguage(L.German);

    [TestMethod]
    public void Without_a_numpad_seen_no_numpad_key_is_offered()
    {
        L.SetLanguage(L.English);
        var list = HotkeyGesture.Suggestions(numpadSeen: false).ToList();
        Assert.IsFalse(list.Any(g => HotkeyGesture.IsNumpadKey(g.VirtualKey)));
        Assert.AreEqual(20, list.Count);
        Assert.AreEqual("Ctrl+Alt+1", list[0].Display);
        Assert.AreEqual("Ctrl+Alt+0", list[9].Display);
        Assert.AreEqual("Ctrl+Alt+Shift+1", list[10].Display);
        Assert.AreEqual("Ctrl+Alt+Shift+0", list[19].Display);
    }

    [TestMethod]
    public void With_a_numpad_seen_the_numpad_comes_first()
    {
        L.SetLanguage(L.German);
        var list = HotkeyGesture.Suggestions(numpadSeen: true).ToList();
        Assert.AreEqual(30, list.Count);
        Assert.AreEqual("Num 1", list[0].Display);
        Assert.AreEqual("Num 0", list[9].Display);
        Assert.AreEqual("Strg+Alt+1", list[10].Display);
    }

    [TestMethod]
    public void Numpad_keys_are_recognised()
    {
        Assert.IsTrue(HotkeyGesture.IsNumpadKey(0x60));  // Num 0
        Assert.IsTrue(HotkeyGesture.IsNumpadKey(0x6B));  // Num +
        Assert.IsFalse(HotkeyGesture.IsNumpadKey(0x31)); // 1 above the letters
        Assert.IsFalse(HotkeyGesture.IsNumpadKey(0x70)); // F1
        Assert.IsFalse(HotkeyGesture.IsNumpadKey(0x90)); // Num Lock itself proves nothing (laptops have it too)
    }
}
