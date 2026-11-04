using Soundboard.Core.Hotkeys;
using Soundboard.Core.Text;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class KeyCapTextTests
{
    [TestCleanup]
    public void BackToGerman() => L.SetLanguage(L.German);

    private static readonly HotkeyGesture Long = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x7B); // F12

    [TestMethod]
    public void German_long_key_shortens_from_the_front_and_keeps_the_key()
    {
        L.SetLanguage(L.German);
        var v = KeyCapText.Variants(Long);
        CollectionAssert.AreEqual(new[] { "Strg+Alt+Umschalt+F12", "…Alt+Umschalt+F12", "…Umschalt+F12", "…F12" }, v.ToArray());
        Assert.AreEqual(Long.Display, v[0]);
    }

    [TestMethod]
    public void A_plus_inside_the_key_name_is_not_a_separator()
    {
        L.SetLanguage(L.English);
        var numPlus = new HotkeyGesture(HotkeyModifiers.Ctrl, 0x6B);
        CollectionAssert.AreEqual(new[] { "Ctrl+Num +", "…Num +" }, KeyCapText.Variants(numPlus).ToArray());
        CollectionAssert.AreEqual(new[] { "Pause" }, KeyCapText.Variants(HotkeyGesture.StopAllDefault).ToArray());
    }

    [TestMethod]
    public void Fit_takes_the_longest_variant_that_fits_else_the_shortest()
    {
        L.SetLanguage(L.English);
        var v = KeyCapText.Variants(Long); // Ctrl+Alt+Shift+F12 (18), …Alt+Shift+F12 (14), …Shift+F12 (10), …F12 (4)
        double W(string s) => s.Length * 7.0;
        Assert.AreEqual("Ctrl+Alt+Shift+F12", KeyCapText.Fit(v, 200, W));
        Assert.AreEqual("…Shift+F12", KeyCapText.Fit(v, 80, W));
        Assert.AreEqual("…F12", KeyCapText.Fit(v, 5, W), "the key itself stays even without room");
        Assert.AreEqual("", KeyCapText.Fit([], 100, W));
    }
}
