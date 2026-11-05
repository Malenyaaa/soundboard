using Soundboard.Core.Audio;
using Soundboard.Core.SystemState;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class CableDefaultResetTests
{
    private const string Spk = "spk", Head = "head", Mic = "mic", CIn = "cin", COut = "cout";

    private sealed class FakeWriter : IDefaultEndpointWriter
    {
        public List<(string Id, DeviceRoleKind Role)> Calls { get; } = [];
        public bool Fail { get; set; }

        public void SetDefaultEndpoint(string deviceId, DeviceRoleKind role)
        {
            if (Fail) throw new System.Runtime.InteropServices.COMException("refused");
            Calls.Add((deviceId, role));
        }
    }

    private static AudioStateReport State(string render, string capture, DeviceStatus spkStatus = DeviceStatus.Active, bool withCable = true)
    {
        var devices = new List<AudioDeviceInfo>
        {
            new(Spk, "Speakers", DeviceFlow.Render, spkStatus, [], 0.5f, false, null),
            new(Head, "Headset", DeviceFlow.Render, DeviceStatus.Active, [], 0.5f, false, null),
            new(Mic, "Microphone", DeviceFlow.Capture, DeviceStatus.Active, [], 0.5f, false, null),
        };
        if (withCable)
        {
            devices.Add(new(CIn, "CABLE Input (VB-Audio Virtual Cable)", DeviceFlow.Render, DeviceStatus.Active, [], 1f, false, null));
            devices.Add(new(COut, "CABLE Output (VB-Audio Virtual Cable)", DeviceFlow.Capture, DeviceStatus.Active, [], 1f, false, null));
        }
        var roles = new[] { DeviceRoleKind.Console, DeviceRoleKind.Multimedia, DeviceRoleKind.Communications };
        var defaults = roles.Select(r => new DefaultEndpoint(DeviceFlow.Render, r, render, devices.FirstOrDefault(d => d.Id == render)?.Name))
            .Concat(roles.Select(r => new DefaultEndpoint(DeviceFlow.Capture, r, capture, devices.FirstOrDefault(d => d.Id == capture)?.Name)))
            .ToList();
        return new AudioStateReport(DateTime.Now, defaults, devices);
    }

    private static readonly AudioSnapshot Saved = AudioSnapshot.From(State(Spk, Mic, withCable: false), "1.0");

    [TestMethod]
    public void Cable_as_default_is_set_back_to_the_saved_device_for_every_role_and_direction()
    {
        var w = new FakeWriter();
        var r = SnapshotService.ResetCableDefaults(Saved, State(CIn, COut), w);
        Assert.AreEqual(6, r.RestoredCount);
        Assert.AreEqual(3, w.Calls.Count(c => c.Id == Spk));
        Assert.AreEqual(3, w.Calls.Count(c => c.Id == Mic));
        CollectionAssert.AreEquivalent(new[] { DeviceRoleKind.Console, DeviceRoleKind.Multimedia, DeviceRoleKind.Communications },
            w.Calls.Where(c => c.Id == Spk).Select(c => c.Role).ToArray());
    }

    [TestMethod]
    public void Saved_device_not_available_means_nothing_is_set()
    {
        var w = new FakeWriter();
        var r = SnapshotService.ResetCableDefaults(Saved, State(CIn, Mic, spkStatus: DeviceStatus.Unplugged), w);
        Assert.AreEqual(0, w.Calls.Count);
        Assert.AreEqual(0, r.RestoredCount);
        Assert.IsTrue(r.Steps.All(s => s.Outcome == RestoreOutcome.SkippedNotAvailable));
        // The "!" still speaks: the cable is the default playback device.
        Assert.IsTrue(AudioHealth.Check(State(CIn, Mic, spkStatus: DeviceStatus.Unplugged), Saved).Any(p => p.Kind == AudioProblemKind.CableDefault));
    }

    [TestMethod]
    public void Another_working_default_chosen_by_the_user_is_left_alone()
    {
        var w = new FakeWriter();
        var r = SnapshotService.ResetCableDefaults(Saved, State(Head, Mic), w);
        Assert.AreEqual(0, r.Steps.Count);
        Assert.AreEqual(0, w.Calls.Count);
    }

    [TestMethod]
    public void A_cable_that_was_already_the_saved_default_stays()
    {
        var savedWithCable = AudioSnapshot.From(State(Spk, COut), "1.0");
        var w = new FakeWriter();
        var r = SnapshotService.ResetCableDefaults(savedWithCable, State(Spk, COut), w);
        Assert.AreEqual(0, r.Steps.Count);
        Assert.AreEqual(0, w.Calls.Count);
    }

    [TestMethod]
    public void Other_vb_audio_devices_chosen_on_purpose_are_left_alone()
    {
        var now = State(Spk, Mic);
        var vm = new AudioDeviceInfo("vm", "Voicemeeter Input (VB-Audio VoiceMeeter VAIO)", DeviceFlow.Render, DeviceStatus.Active, [], 1f, false, null);
        now = now with
        {
            Devices = [.. now.Devices, vm],
            Defaults = [.. now.Defaults.Select(d => d.Flow == DeviceFlow.Render ? d with { Id = "vm", Name = vm.Name } : d)],
        };
        var w = new FakeWriter();
        Assert.AreEqual(0, SnapshotService.ResetCableDefaults(Saved, now, w).Steps.Count);
        Assert.AreEqual(0, w.Calls.Count);
    }

    [TestMethod]
    public void Roles_refused_by_the_caller_are_skipped()
    {
        var w = new FakeWriter();
        SnapshotService.ResetCableDefaults(Saved, State(CIn, Mic), w, (flow, role) => role != DeviceRoleKind.Console);
        Assert.AreEqual(2, w.Calls.Count);
        Assert.IsFalse(w.Calls.Any(c => c.Role == DeviceRoleKind.Console));
    }

    [TestMethod]
    public void A_refused_write_is_reported_as_failed_not_thrown()
    {
        var r = SnapshotService.ResetCableDefaults(Saved, State(CIn, Mic), new FakeWriter { Fail = true });
        Assert.AreEqual(3, r.Steps.Count(s => s.Outcome == RestoreOutcome.Failed));
    }

    [TestMethod]
    public void The_state_after_a_reset_no_longer_shows_the_cable_as_default()
    {
        var now = State(CIn, Mic);
        var r = SnapshotService.ResetCableDefaults(Saved, now, new FakeWriter());
        var after = SnapshotService.WithRestoredDefaults(now, r, Saved);
        Assert.AreEqual(Spk, after.GetDefault(DeviceFlow.Render, DeviceRoleKind.Multimedia)!.Id);
        Assert.AreEqual(0, AudioHealth.Check(after, Saved).Count);
    }
}
