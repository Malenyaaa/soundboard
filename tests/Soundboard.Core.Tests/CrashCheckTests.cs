using Soundboard.Core.Audio;
using Soundboard.Core.SystemState;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class CrashCheckTests
{
    private static AudioStateReport State(string? renderDefault, bool spkMuted = false)
    {
        var devices = new List<AudioDeviceInfo>
        {
            new("spk", "Lautsprecher", DeviceFlow.Render, DeviceStatus.Active, [], 0.5f, spkMuted, null),
            new("head", "Headset", DeviceFlow.Render, DeviceStatus.Active, [], 0.5f, false, null),
        };
        var name = devices.FirstOrDefault(d => d.Id == renderDefault)?.Name;
        var defaults = new[] { DeviceRoleKind.Console, DeviceRoleKind.Multimedia, DeviceRoleKind.Communications }
            .Select(r => new DefaultEndpoint(DeviceFlow.Render, r, renderDefault, name)).ToList();
        return new AudioStateReport(DateTime.Now, defaults, devices);
    }

    private static readonly AudioSnapshot Saved = AudioSnapshot.From(State("spk"), "1.0");

    [TestMethod]
    public void Normal_exit_reports_nothing()
    {
        bool read = false;
        var r = CrashCheck.Evaluate(null, 42, Saved, () => { read = true; return State("spk"); });
        Assert.IsNull(r);
        Assert.IsFalse(read, "nothing is read after a normal exit");
    }

    [TestMethod]
    public void Flag_of_another_instance_is_ignored()
    {
        Assert.IsNull(CrashCheck.Evaluate(new DirtyInfo(7, DateTime.Now, true), 42, Saved, () => State("spk")));
    }

    [TestMethod]
    public void Crash_with_cable_explains_discord_silence_and_way_back()
    {
        var r = CrashCheck.Evaluate(new DirtyInfo(42, DateTime.Now, true), 42, Saved, () => State("spk"))!;
        var text = string.Join("\n", r.Lines("In Discord wieder „Headset-Mikro“ wählen."));
        StringAssert.Contains(text, "niemand füttert es");
        StringAssert.Contains(text, "Headset-Mikro");
        Assert.AreEqual(0, r.Problems.Count);
        Assert.IsFalse(r.CanRestore);
        Assert.IsFalse(text.Contains("Windows-Audio"), "nothing to say about working Windows audio");
    }

    [TestMethod]
    public void Crash_with_a_working_other_default_stays_silent()
    {
        // The user chose the headset himself: it works, so no window.
        Assert.IsNull(CrashCheck.Evaluate(new DirtyInfo(42, DateTime.Now, false), 42, Saved, () => State("head")));
    }

    [TestMethod]
    public void Crash_without_cable_but_without_default_device_shows_the_problem()
    {
        var r = CrashCheck.Evaluate(new DirtyInfo(42, DateTime.Now, false), 42, Saved, () => State(null))!;
        Assert.IsNotNull(r);
        Assert.IsTrue(r.CanRestore);
        var text = string.Join("\n", r.Lines("x"));
        Assert.IsFalse(text.Contains("niemand füttert es"));
        StringAssert.Contains(text, "„Lautsprecher“ ist nicht mehr da");
    }

    [TestMethod]
    public void Crash_with_muted_default_shows_the_problem_without_restore()
    {
        var r = CrashCheck.Evaluate(new DirtyInfo(42, DateTime.Now, false), 42, Saved, () => State("spk", spkMuted: true))!;
        Assert.AreEqual(AudioProblemKind.Muted, r.Problems.Single().Kind);
        Assert.IsFalse(r.CanRestore, "the defaults are as saved – restoring would not help");
    }

    [TestMethod]
    public void Unreadable_flag_counts_as_crash_but_says_nothing_when_all_works()
    {
        Assert.IsNull(CrashCheck.Evaluate(new DirtyInfo(0, DateTime.MinValue, false), 42, null, () => State("spk")));
        Assert.IsNotNull(CrashCheck.Evaluate(new DirtyInfo(0, DateTime.MinValue, true), 42, null, () => State("spk")));
    }
}
