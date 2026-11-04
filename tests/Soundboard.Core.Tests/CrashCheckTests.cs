using Soundboard.Core.Audio;
using Soundboard.Core.SystemState;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class CrashCheckTests
{
    private static AudioStateReport State(string renderDefault)
    {
        var devices = new List<AudioDeviceInfo>
        {
            new("spk", "Lautsprecher", DeviceFlow.Render, DeviceStatus.Active, [], 0.5f, false, null),
            new("head", "Headset", DeviceFlow.Render, DeviceStatus.Active, [], 0.5f, false, null),
        };
        var name = devices.First(d => d.Id == renderDefault).Name;
        var defaults = new[] { DeviceRoleKind.Console, DeviceRoleKind.Multimedia, DeviceRoleKind.Communications }
            .Select(r => new DefaultEndpoint(DeviceFlow.Render, r, renderDefault, name)).ToList();
        return new AudioStateReport(DateTime.Now, defaults, devices);
    }

    [TestMethod]
    public void Normal_exit_reports_nothing()
    {
        bool compared = false;
        var r = CrashCheck.Evaluate(null, 42, () => { compared = true; return null; });
        Assert.IsNull(r);
        Assert.IsFalse(compared, "nothing is read after a normal exit");
    }

    [TestMethod]
    public void Flag_of_another_instance_is_ignored()
    {
        Assert.IsNull(CrashCheck.Evaluate(new DirtyInfo(7, DateTime.Now, true), 42, () => null));
    }

    [TestMethod]
    public void Crash_with_cable_explains_discord_silence_and_way_back()
    {
        var snap = AudioSnapshot.From(State("spk"), "1.0");
        var r = CrashCheck.Evaluate(new DirtyInfo(42, DateTime.Now, true), 42, () => SnapshotService.Compare(snap, State("spk")))!;
        var text = string.Join("\n", r.Lines("In Discord wieder „Headset-Mikro“ wählen."));
        StringAssert.Contains(text, "niemand füttert es");
        StringAssert.Contains(text, "Headset-Mikro");
        StringAssert.Contains(text, "wie gesichert");
        Assert.IsFalse(r.DefaultsChanged);
    }

    [TestMethod]
    public void Crash_without_cable_but_changed_default_offers_restore()
    {
        var snap = AudioSnapshot.From(State("spk"), "1.0");
        var r = CrashCheck.Evaluate(new DirtyInfo(42, DateTime.Now, false), 42, () => SnapshotService.Compare(snap, State("head")))!;
        Assert.IsTrue(r.DefaultsChanged);
        var text = string.Join("\n", r.Lines("x"));
        Assert.IsFalse(text.Contains("niemand füttert es"));
        StringAssert.Contains(text, "war „Lautsprecher“, jetzt „Headset“");
    }

    [TestMethod]
    public void Unreadable_flag_counts_as_crash()
    {
        var r = CrashCheck.Evaluate(new DirtyInfo(0, DateTime.MinValue, false), 42, () => null);
        Assert.IsNotNull(r);
        // Without a saved state the window says nothing about comparing (there is nothing to compare).
        Assert.AreEqual(0, r.Lines("x").Count);
    }
}
