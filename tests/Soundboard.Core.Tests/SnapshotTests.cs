using Soundboard.Core.Audio;
using Soundboard.Core.SystemState;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class SnapshotTests
{
    private static AudioDeviceInfo Dev(string id, string name, DeviceFlow flow, DeviceStatus status = DeviceStatus.Active, float vol = 0.5f) =>
        new(id, name, flow, status, [], vol, false, null);

    /// <summary>Speakers + headset (render), mic (capture); defaults as given.</summary>
    private static AudioStateReport State(string renderDefault = "spk", string commRender = "head", string capture = "mic",
        IEnumerable<AudioDeviceInfo>? extra = null, float spkVol = 0.5f)
    {
        var devices = new List<AudioDeviceInfo>
        {
            Dev("spk", "Lautsprecher", DeviceFlow.Render, vol: spkVol),
            Dev("head", "Headset", DeviceFlow.Render),
            Dev("mic", "Mikrofon", DeviceFlow.Capture),
        };
        if (extra is not null) devices.AddRange(extra);
        string? Name(string? id) => devices.FirstOrDefault(d => d.Id == id)?.Name;
        var defaults = new List<DefaultEndpoint>
        {
            new(DeviceFlow.Render, DeviceRoleKind.Console, renderDefault, Name(renderDefault)),
            new(DeviceFlow.Render, DeviceRoleKind.Multimedia, renderDefault, Name(renderDefault)),
            new(DeviceFlow.Render, DeviceRoleKind.Communications, commRender, Name(commRender)),
            new(DeviceFlow.Capture, DeviceRoleKind.Console, capture, Name(capture)),
            new(DeviceFlow.Capture, DeviceRoleKind.Multimedia, capture, Name(capture)),
            new(DeviceFlow.Capture, DeviceRoleKind.Communications, capture, Name(capture)),
        };
        return new AudioStateReport(new DateTime(2026, 11, 4, 12, 0, 0), defaults, devices);
    }

    private static readonly AudioDeviceInfo CableIn = Dev("cin", "CABLE Input (VB-Audio Virtual Cable)", DeviceFlow.Render);
    private static readonly AudioDeviceInfo CableOut = Dev("cout", "CABLE Output (VB-Audio Virtual Cable)", DeviceFlow.Capture);

    private sealed class FakeWriter : IDefaultEndpointWriter
    {
        public readonly List<(string Id, DeviceRoleKind Role)> Calls = [];
        public string? FailFor;

        public void SetDefaultEndpoint(string deviceId, DeviceRoleKind role)
        {
            if (deviceId == FailFor) throw new System.Runtime.InteropServices.COMException("geht nicht");
            Calls.Add((deviceId, role));
        }
    }

    [TestMethod]
    public void Identical_state_has_no_differences()
    {
        var snap = AudioSnapshot.From(State(), "1.0");
        var cmp = SnapshotService.Compare(snap, State());
        Assert.IsTrue(cmp.IsIdentical);
        Assert.IsFalse(cmp.DefaultsChanged);
        Assert.AreEqual(6, cmp.Rows.Count, "the six default roles are always listed");
        Assert.AreEqual(3, cmp.UnchangedDevices);
    }

    [TestMethod]
    public void Changed_default_is_reported()
    {
        var snap = AudioSnapshot.From(State(), "1.0");
        var cmp = SnapshotService.Compare(snap, State(renderDefault: "head"));
        Assert.IsTrue(cmp.DefaultsChanged);
        Assert.AreEqual(2, cmp.DifferenceCount, "console and multimedia");
        StringAssert.Contains(cmp.DefaultChangeLines.First(), "war „Lautsprecher“, jetzt „Headset“");
        Assert.IsFalse(cmp.CableBecameDefault);
    }

    [TestMethod]
    public void Driver_install_new_cable_as_default_is_detected()
    {
        var snap = AudioSnapshot.From(State(), "1.0");
        var now = State(renderDefault: "cin", capture: "cout", extra: [CableIn, CableOut]);
        var cmp = SnapshotService.Compare(snap, now);
        Assert.IsTrue(cmp.CableBecameDefault);
        Assert.AreEqual(2, cmp.Rows.Count(r => r.Kind == ComparisonKind.Added));
    }

    [TestMethod]
    public void New_cable_alone_is_not_a_default_change()
    {
        var snap = AudioSnapshot.From(State(), "1.0");
        var cmp = SnapshotService.Compare(snap, State(extra: [CableIn, CableOut]));
        Assert.IsFalse(cmp.DefaultsChanged);
        Assert.AreEqual(2, cmp.DifferenceCount);
    }

    [TestMethod]
    public void Volume_change_and_missing_device_are_listed()
    {
        var snap = AudioSnapshot.From(State(extra: [Dev("usb", "USB-Kopfhörer", DeviceFlow.Render)]), "1.0");
        var cmp = SnapshotService.Compare(snap, State(spkVol: 0.8f));
        Assert.IsTrue(cmp.Rows.Any(r => r.What.Contains("Lautstärke") && r.Saved == "50\u202F%" && r.Now == "80\u202F%"));
        Assert.IsTrue(cmp.Rows.Any(r => r.Kind == ComparisonKind.Missing && r.What.Contains("USB-Kopfhörer")));
    }

    [TestMethod]
    public void Restore_sets_only_the_saved_ids_where_they_differ()
    {
        var snap = AudioSnapshot.From(State(), "1.0");
        var w = new FakeWriter();
        var r = SnapshotService.Restore(snap, State(renderDefault: "head"), w);

        CollectionAssert.AreEquivalent(new[] { ("spk", DeviceRoleKind.Console), ("spk", DeviceRoleKind.Multimedia) }, w.Calls);
        Assert.AreEqual(2, r.RestoredCount);
        Assert.IsTrue(r.AllGood);
    }

    [TestMethod]
    public void Restore_does_nothing_when_all_is_the_same()
    {
        var w = new FakeWriter();
        var r = SnapshotService.Restore(AudioSnapshot.From(State(), "1.0"), State(), w);
        Assert.AreEqual(0, w.Calls.Count);
        Assert.IsTrue(r.Steps.All(s => s.Outcome == RestoreOutcome.AlreadySame));
    }

    [TestMethod]
    public void Restore_after_driver_install_puts_back_real_devices_never_the_cable()
    {
        var snap = AudioSnapshot.From(State(), "1.0");
        var now = State(renderDefault: "cin", capture: "cout", extra: [CableIn, CableOut]);
        var w = new FakeWriter();
        SnapshotService.Restore(snap, now, w);

        Assert.IsTrue(w.Calls.All(c => c.Id is "spk" or "mic"), "only IDs from the snapshot");
        Assert.AreEqual(5, w.Calls.Count, "2 render roles + 3 capture roles");
    }

    [TestMethod]
    public void Restore_never_makes_a_cable_default_even_if_saved()
    {
        // A snapshot taken while the cable was (wrongly) default must not put it back.
        var snap = AudioSnapshot.From(State(renderDefault: "cin", extra: [CableIn]), "1.0");
        var w = new FakeWriter();
        var r = SnapshotService.Restore(snap, State(extra: [CableIn]), w);

        Assert.AreEqual(0, w.Calls.Count);
        Assert.AreEqual(2, r.Steps.Count(s => s.Outcome == RestoreOutcome.SkippedCable));
        StringAssert.Contains(r.ManualSteps(), "nie zum Standard");
    }

    [TestMethod]
    public void Restore_skips_saved_device_that_is_gone_and_explains()
    {
        var snap = AudioSnapshot.From(State(renderDefault: "usb", extra: [Dev("usb", "USB-Kopfhörer", DeviceFlow.Render)]), "1.0");
        var w = new FakeWriter();
        var r = SnapshotService.Restore(snap, State(), w);

        Assert.AreEqual(0, w.Calls.Count);
        Assert.IsFalse(r.AllGood);
        StringAssert.Contains(r.ManualSteps(), "USB-Kopfhörer");
        StringAssert.Contains(r.ManualSteps(), "nicht angeschlossen");
    }

    [TestMethod]
    public void Restore_skips_disabled_device()
    {
        var snap = AudioSnapshot.From(State(renderDefault: "usb", extra: [Dev("usb", "USB", DeviceFlow.Render)]), "1.0");
        var w = new FakeWriter();
        SnapshotService.Restore(snap, State(extra: [Dev("usb", "USB", DeviceFlow.Render, DeviceStatus.Disabled)]), w);
        Assert.AreEqual(0, w.Calls.Count);
    }

    [TestMethod]
    public void Failed_restore_gives_manual_instructions()
    {
        var snap = AudioSnapshot.From(State(), "1.0");
        var w = new FakeWriter { FailFor = "spk" };
        var r = SnapshotService.Restore(snap, State(renderDefault: "head"), w);

        Assert.IsFalse(r.AllGood);
        Assert.AreEqual(2, r.Steps.Count(s => s.Outcome == RestoreOutcome.Failed));
        StringAssert.Contains(r.ManualSteps(), "Windows-Einstellungen → System → Sound");
        StringAssert.Contains(SnapshotService.Format(r), "fehlgeschlagen");
    }

    [TestMethod]
    public void Snapshot_json_round_trip_and_backup()
    {
        using var dir = new TempDir();
        var path = dir.File("audio-snapshot.json");
        var a = AudioSnapshot.From(State(), "1.0");
        SnapshotStore.Save(path, a);
        SnapshotStore.Save(path, AudioSnapshot.From(State(renderDefault: "head"), "1.1"));

        var loaded = SnapshotStore.Load(path)!;
        Assert.AreEqual("1.1", loaded.AppVersion);
        Assert.AreEqual("head", loaded.GetDefault(DeviceFlow.Render, DeviceRoleKind.Console)!.Id);
        Assert.AreEqual(3, loaded.Devices.Count);
        var backup = SnapshotStore.Load(dir.File("audio-snapshot.vorher.json"))!;
        Assert.AreEqual("1.0", backup.AppVersion);
        StringAssert.Contains(File.ReadAllText(path), "\"Communications\"", "enums as text");
    }

    [TestMethod]
    public void Broken_snapshot_file_gives_readable_error()
    {
        using var dir = new TempDir();
        var path = dir.File("audio-snapshot.json");
        File.WriteAllText(path, "{ kaputt");
        var ex = Assert.ThrowsException<SnapshotException>(() => SnapshotStore.Load(path));
        StringAssert.Contains(ex.Message, "ließ sich nicht lesen");
        Assert.IsNull(SnapshotStore.Load(dir.File("fehlt.json")));
    }

    [TestMethod]
    public void Dirty_flag_detects_ended_process()
    {
        using var dir = new TempDir();
        var flag = new DirtyFlag(dir.File("running.json"));
        Assert.IsNull(flag.Read());

        var started = new DateTime(2026, 11, 4, 10, 0, 0);
        flag.Write(new DirtyInfo(1234, started, true));
        var info = flag.Read()!;
        Assert.IsTrue(info.CableActive);

        Assert.IsFalse(DirtyFlag.IsStale(info, _ => started), "still running");
        Assert.IsTrue(DirtyFlag.IsStale(info, _ => null), "process gone");
        Assert.IsTrue(DirtyFlag.IsStale(info, _ => started.AddHours(1)), "PID reused by another program");

        flag.Delete();
        Assert.IsNull(flag.Read());
    }

    [TestMethod]
    public void Cable_detector_finds_both_ends_and_ignores_others()
    {
        var c = CableDetector.Detect(State(extra: [CableIn, CableOut, Dev("a", "CABLE-A Input (VB-Audio Cable A)", DeviceFlow.Render)]));
        Assert.IsTrue(c.IsReady);
        Assert.AreEqual("cin", c.Input!.Id);
        Assert.AreEqual("cout", c.Output!.Id);
        Assert.IsFalse(CableDetector.Detect(State()).IsKnown);
        Assert.IsTrue(CableDetector.IsCableName("CABLE Output (VB-Audio Virtual Cable)"));
        Assert.IsFalse(CableDetector.IsCableName("Mikrofon"));
    }
}
