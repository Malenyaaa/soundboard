using Soundboard.Core.Audio;
using Soundboard.Core.SystemState;

namespace Soundboard.Core.Tests;

/// <summary>"No longer works" (the "!" in the status line) against "only different" (quiet).</summary>
[TestClass]
public sealed class AudioHealthTests
{
    private static AudioDeviceInfo Dev(string id, string name, DeviceFlow flow, DeviceStatus status = DeviceStatus.Active,
        float? vol = 0.5f, bool? muted = false) => new(id, name, flow, status, [], vol, muted, null);

    private static readonly AudioDeviceInfo Spk = Dev("spk", "Lautsprecher", DeviceFlow.Render);
    private static readonly AudioDeviceInfo Head = Dev("head", "Kopfhörer", DeviceFlow.Render);
    private static readonly AudioDeviceInfo Mic = Dev("mic", "Mikrofon", DeviceFlow.Capture);
    private static readonly AudioDeviceInfo CableIn = Dev("cin", "CABLE Input (VB-Audio Virtual Cable)", DeviceFlow.Render);
    private static readonly AudioDeviceInfo CableOut = Dev("cout", "CABLE Output (VB-Audio Virtual Cable)", DeviceFlow.Capture);

    /// <summary>All three roles of a direction on one device (null = Windows has none).</summary>
    private static AudioStateReport State(string? render, string? capture, params AudioDeviceInfo[] devices)
    {
        string? Name(string? id) => devices.FirstOrDefault(d => d.Id == id)?.Name;
        var defaults = new List<DefaultEndpoint>();
        foreach (var role in new[] { DeviceRoleKind.Console, DeviceRoleKind.Multimedia, DeviceRoleKind.Communications })
        {
            defaults.Add(new(DeviceFlow.Render, role, render, Name(render)));
            defaults.Add(new(DeviceFlow.Capture, role, capture, Name(capture)));
        }
        return new AudioStateReport(new DateTime(2026, 11, 5, 12, 0, 0), defaults, devices);
    }

    private static readonly AudioSnapshot Saved = AudioSnapshot.From(State("spk", "mic", Spk, Head, Mic), "1.0");

    [TestMethod]
    public void Same_as_saved_is_fine()
    {
        Assert.AreEqual(0, AudioHealth.Check(State("spk", "mic", Spk, Head, Mic), Saved).Count);
    }

    [TestMethod]
    public void Another_working_device_chosen_by_the_user_is_only_different()
    {
        var now = State("head", "mic", Spk, Head, Mic);
        Assert.AreEqual(0, AudioHealth.Check(now, Saved).Count);
        Assert.IsTrue(SnapshotService.Compare(Saved, now).DefaultsChanged, "still listed in the tab");
    }

    [TestMethod]
    public void Saved_device_gone_and_windows_has_no_default_is_a_problem()
    {
        // E.g. a virtual device of audio software that does not run: gone, and nothing took its place.
        var p = AudioHealth.Check(State(null, "mic", Head with { Status = DeviceStatus.Disabled }, Mic), Saved);
        Assert.AreEqual(AudioProblemKind.NoDevice, p.Single().Kind);
        Assert.AreEqual(DeviceFlow.Render, p.Single().Flow);
        CollectionAssert.AreEqual(new[] { DeviceRoleKind.Multimedia, DeviceRoleKind.Communications }, p.Single().Roles.ToArray(),
            "both roles on the same device make one line");
        StringAssert.Contains(p.Single().Text, "„Lautsprecher“");
    }

    [TestMethod]
    public void Saved_device_gone_but_windows_switched_to_a_working_one_is_fine()
    {
        Assert.AreEqual(0, AudioHealth.Check(State("head", "mic", Head, Mic), Saved).Count);
    }

    [TestMethod]
    public void Default_device_that_is_disabled_or_unplugged_is_a_problem()
    {
        foreach (var status in new[] { DeviceStatus.Disabled, DeviceStatus.Unplugged, DeviceStatus.NotPresent })
        {
            var p = AudioHealth.Check(State("spk", "mic", Spk with { Status = status }, Head, Mic), Saved);
            Assert.AreEqual(AudioProblemKind.NotActive, p.Single().Kind, status.ToString());
            StringAssert.Contains(p.Single().Text, AudioStateText.Status(status));
        }
    }

    [TestMethod]
    public void Cable_as_default_playback_is_a_problem_even_if_saved_so()
    {
        var p = AudioHealth.Check(State("cin", "mic", Spk, Mic, CableIn, CableOut), Saved);
        Assert.AreEqual(AudioProblemKind.CableDefault, p.Single().Kind);
        StringAssert.Contains(p.Single().Text, "du hörst nichts");

        var savedWithCable = AudioSnapshot.From(State("cin", "mic", Spk, Mic, CableIn), "1.0");
        Assert.AreEqual(1, AudioHealth.Check(State("cin", "mic", Spk, Mic, CableIn), savedWithCable).Count, "into the cable is never heard");
    }

    [TestMethod]
    public void Cable_as_default_microphone_only_when_it_became_so()
    {
        var p = AudioHealth.Check(State("spk", "cout", Spk, Mic, CableOut), Saved);
        Assert.AreEqual(AudioProblemKind.CableDefault, p.Single().Kind);
        Assert.AreEqual(DeviceFlow.Capture, p.Single().Flow);

        var chosen = AudioSnapshot.From(State("spk", "cout", Spk, Mic, CableOut), "1.0");
        Assert.AreEqual(0, AudioHealth.Check(State("spk", "cout", Spk, Mic, CableOut), chosen).Count, "chosen on purpose before");
    }

    [TestMethod]
    public void Muted_or_zero_volume_playback_default_is_a_problem()
    {
        Assert.AreEqual(AudioProblemKind.Muted,
            AudioHealth.Check(State("spk", "mic", Spk with { Muted = true }, Mic), Saved).Single().Kind);
        Assert.AreEqual(AudioProblemKind.Silent,
            AudioHealth.Check(State("spk", "mic", Spk with { VolumeScalar = 0f }, Mic), Saved).Single().Kind);
        Assert.AreEqual(0, AudioHealth.Check(State("spk", "mic", Spk with { VolumeScalar = 0.01f }, Mic), Saved).Count, "quiet is not silent");
    }

    [TestMethod]
    public void Muted_microphone_or_unreadable_volume_is_no_alarm()
    {
        Assert.AreEqual(0, AudioHealth.Check(State("spk", "mic", Spk, Mic with { Muted = true }), Saved).Count);
        Assert.AreEqual(0, AudioHealth.Check(State("spk", "mic", Spk with { VolumeScalar = null, Muted = null }, Mic), Saved).Count);
    }

    [TestMethod]
    public void Without_a_saved_state_a_missing_default_is_not_reported()
    {
        // No microphone at all may simply be how this PC is.
        Assert.AreEqual(0, AudioHealth.Check(State("spk", null, Spk), null).Count);
        // The cable as playback default still is.
        Assert.AreEqual(1, AudioHealth.Check(State("cin", null, Spk, CableIn), null).Count);
    }
}
