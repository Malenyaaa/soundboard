using Soundboard.Core.Hotkeys;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class HotkeyTests
{
    private sealed class FakeApi : IHotkeyApi
    {
        public readonly Dictionary<int, HotkeyGesture> Registered = [];
        public readonly HashSet<HotkeyGesture> TakenElsewhere = [];

        public HotkeyRegisterResult Register(int id, HotkeyGesture gesture)
        {
            if (TakenElsewhere.Contains(gesture)) return HotkeyRegisterResult.TakenByOtherProgram;
            if (Registered.ContainsValue(gesture)) return HotkeyRegisterResult.Failed; // Windows refuses doubles
            Registered[id] = gesture;
            return HotkeyRegisterResult.Ok;
        }

        public void Unregister(int id) => Registered.Remove(id);

        public int IdOf(HotkeyGesture g) => Registered.First(kv => kv.Value == g).Key;
    }

    private static readonly HotkeyGesture Num1 = new(HotkeyModifiers.None, 0x61);
    private static readonly HotkeyGesture Num2 = new(HotkeyModifiers.None, 0x62);

    [TestMethod]
    public void Token_round_trip_and_german_display()
    {
        var g = new HotkeyGesture(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x61);
        Assert.AreEqual("Ctrl+Alt+NumPad1", g.ToToken());
        Assert.AreEqual("Strg+Alt+Num 1", g.Display);
        Assert.AreEqual(g, HotkeyGesture.Parse("Ctrl+Alt+NumPad1"));
        Assert.AreEqual(HotkeyGesture.StopAllDefault, HotkeyGesture.Parse("Pause"));
        Assert.AreEqual(new HotkeyGesture(HotkeyModifiers.Shift, 0xE2), HotkeyGesture.Parse("Shift+VKE2"));
        Assert.IsNull(HotkeyGesture.Parse(""));
        Assert.IsNull(HotkeyGesture.Parse("Hyper+A"));
    }

    [TestMethod]
    public void Numpad_suggestions_start_with_num1()
    {
        var first = HotkeyGesture.NumpadSuggestions().Take(10).Select(g => g.Display).ToList();
        CollectionAssert.AreEqual(new[] { "Num 1", "Num 2", "Num 3", "Num 4", "Num 5", "Num 6", "Num 7", "Num 8", "Num 9", "Num 0" }, first);
    }

    [TestMethod]
    public void Registers_and_triggers_owner()
    {
        var api = new FakeApi();
        var s = new HotkeyService(api);
        string? hit = null;
        s.Triggered += (_, o) => hit = o;

        s.SetAll([("a", Num1), ("b", Num2), ("c", null)]);
        s.HandleHotkey(api.IdOf(Num2));

        Assert.AreEqual("b", hit);
        Assert.AreEqual(HotkeyState.Active, s.Get("a")!.State);
        Assert.AreEqual(HotkeyState.None, s.Get("c")!.State);
    }

    [TestMethod]
    public void Same_key_twice_first_wins_second_is_duplicate()
    {
        var api = new FakeApi();
        var s = new HotkeyService(api);

        s.SetAll([("a", Num1), ("b", Num1)]);

        Assert.AreEqual(HotkeyState.Active, s.Get("a")!.State);
        var b = s.Get("b")!;
        Assert.AreEqual(HotkeyState.Duplicate, b.State);
        Assert.AreEqual("a", b.DuplicateOf);
        Assert.AreEqual(1, api.Registered.Count);

        // Removing the first one hands the key to the second.
        s.Remove("a");
        Assert.AreEqual(HotkeyState.Active, s.Get("b")!.State);
    }

    [TestMethod]
    public void Key_of_other_program_is_reported_not_forced()
    {
        var api = new FakeApi();
        api.TakenElsewhere.Add(Num1);
        var s = new HotkeyService(api);

        s.SetAll([("a", Num1)]);

        Assert.AreEqual(HotkeyState.TakenByOtherProgram, s.Get("a")!.State);
        Assert.AreEqual(0, api.Registered.Count);
    }

    [TestMethod]
    public void Pause_releases_all_keys_and_ignores_presses()
    {
        var api = new FakeApi();
        var s = new HotkeyService(api);
        int hits = 0;
        s.Triggered += (_, _) => hits++;
        s.SetAll([("a", Num1)]);
        int id = api.IdOf(Num1);

        s.Paused = true;
        s.HandleHotkey(id);

        Assert.AreEqual(0, api.Registered.Count);
        Assert.AreEqual(HotkeyState.Paused, s.Get("a")!.State);
        Assert.AreEqual(0, hits);

        s.Paused = false;
        Assert.AreEqual(1, api.Registered.Count);
        s.HandleHotkey(api.IdOf(Num1));
        Assert.AreEqual(1, hits);
    }

    [TestMethod]
    public void Changing_a_key_unregisters_the_old_one()
    {
        var api = new FakeApi();
        var s = new HotkeyService(api);
        s.SetAll([("a", Num1)]);

        s.Set("a", Num2);

        Assert.AreEqual(1, api.Registered.Count);
        Assert.AreEqual(Num2, api.Registered.Values.Single());
        Assert.AreEqual("a", s.FindOwner(Num2));
        Assert.IsNull(s.FindOwner(Num2, exceptOwner: "a"));
    }

    [TestMethod]
    public void Dispose_unregisters_everything()
    {
        var api = new FakeApi();
        var s = new HotkeyService(api);
        s.SetAll([("a", Num1), ("b", Num2)]);
        s.Dispose();
        Assert.AreEqual(0, api.Registered.Count);
    }
}
