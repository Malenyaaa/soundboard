using System.Buffers.Binary;
using Soundboard.Core.Audio;
using Soundboard.Core.SystemState;

namespace Soundboard.Core.Tests;

/// <summary>Endpoint formats in memory: the tests never call IPolicyConfig.</summary>
internal sealed class FakeFormats : IDeviceFormatApi
{
    public Dictionary<string, (EndpointIdentity Who, EndpointFormats Formats)> Devices { get; } = [];
    public List<string> Writes { get; } = [];
    public bool Refuse { get; set; }

    /// <summary>Windows accepts the call but keeps the old rate.</summary>
    public bool Ignore { get; set; }

    public Action? BeforeWrite { get; set; }

    public void Add(string id, string name, DeviceFlow flow, int rate, DeviceStatus status = DeviceStatus.Active) =>
        Devices[id] = (new EndpointIdentity(name, flow, status), new EndpointFormats(Format(rate, 32, 2), Format(rate, 32, 2)));

    public EndpointIdentity? Find(string deviceId) => Devices.TryGetValue(deviceId, out var d) ? d.Who : null;

    public EndpointFormats? Read(string deviceId) =>
        Devices.TryGetValue(deviceId, out var d) && d.Who.Status == DeviceStatus.Active ? d.Formats : null;

    public void Write(string deviceId, byte[] deviceFormat, byte[] mixFormat)
    {
        BeforeWrite?.Invoke();
        if (Refuse) throw new System.Runtime.InteropServices.COMException("refused");
        Writes.Add(deviceId);
        if (Ignore) return;
        var d = Devices[deviceId];
        Devices[deviceId] = (d.Who, new EndpointFormats(deviceFormat, mixFormat));
    }

    public int RateOf(string id) => Devices[id].Formats.SampleRate;

    /// <summary>A WAVEFORMATEXTENSIBLE (40 bytes) as Windows hands it out.</summary>
    public static byte[] Format(int rate, int bits, int channels)
    {
        var f = new byte[40];
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0), 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(2), (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(4), (uint)rate);
        int block = channels * bits / 8;
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(8), (uint)(rate * block));
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(12), (ushort)block);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(14), (ushort)bits);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(16), 22);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(18), (ushort)bits);
        BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(20), 3); // front left + right
        f[24] = 3; // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT starts with 3
        return f;
    }
}

[TestClass]
public sealed class CableFormatTests
{
    private const string CIn = "cin", COut = "cout", Spk = "spk";
    private string _dir = "";
    private ChangeJournal _journal = null!;
    private FakeFormats _formats = null!;
    private SystemChanges _changes = null!;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sb-format-" + Guid.NewGuid().ToString("N"));
        _journal = new ChangeJournal(Path.Combine(_dir, "changes.json"));
        _formats = new FakeFormats();
        _formats.Add(CIn, "CABLE Input (VB-Audio Virtual Cable)", DeviceFlow.Render, 44100);
        _formats.Add(COut, "CABLE Output (VB-Audio Virtual Cable)", DeviceFlow.Capture, 44100);
        _formats.Add(Spk, "Speakers", DeviceFlow.Render, 44100);
        _changes = new SystemChanges(_journal, new FakeRegistry(), _formats);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { }
    }

    private static AudioDeviceInfo Dev(string id, string name, DeviceFlow flow) =>
        new(id, name, flow, DeviceStatus.Active, [], 1f, false, null);

    private static readonly CableInfo Cable = new(
        Dev(CIn, "CABLE Input (VB-Audio Virtual Cable)", DeviceFlow.Render),
        Dev(COut, "CABLE Output (VB-Audio Virtual Cable)", DeviceFlow.Capture));

    [TestMethod]
    public void Rate_change_keeps_channels_bits_and_mask()
    {
        var f = FakeFormats.Format(44100, 24, 2);
        var g = WaveFormatBytes.WithRate(f, 48000);
        Assert.AreEqual(48000, WaveFormatBytes.Rate(g));
        Assert.AreEqual(2, WaveFormatBytes.Channels(g));
        Assert.AreEqual(24, WaveFormatBytes.Bits(g));
        Assert.AreEqual(48000 * 6, (int)BinaryPrimitives.ReadUInt32LittleEndian(g.AsSpan(8)));
        CollectionAssert.AreEqual(f[12..], g[12..]);
        Assert.AreEqual(44100, WaveFormatBytes.Rate(f), "the original is not changed");
    }

    [TestMethod]
    public void Both_cable_ends_go_to_48k_and_the_old_format_is_journaled_first()
    {
        bool ahead = false;
        _formats.BeforeWrite = () =>
        {
            ahead = new ChangeJournal(_journal.Path).Read().Any(e => e.Kind == ChangeKind.CableFormat && e.OldRate == 44100 && !e.Applied);
        };
        var r = _changes.EnsureCableFormat(Cable);
        Assert.IsTrue(ahead, "entry on disk before the format changes");
        Assert.IsTrue(r.Changed);
        Assert.IsTrue(r.AllAtRate(48000));
        Assert.AreEqual(48000, _formats.RateOf(CIn));
        Assert.AreEqual(48000, _formats.RateOf(COut));
        var entries = _journal.Read();
        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries.All(e => e.Scope == ChangeScope.Install && e.Applied));
    }

    [TestMethod]
    public void A_real_device_is_never_reformatted()
    {
        // A cable record that points at a real device (wrong name, or Windows says another name).
        var fake = new CableInfo(Dev(Spk, "Speakers", DeviceFlow.Render), Dev(COut, "CABLE Output (VB-Audio Virtual Cable)", DeviceFlow.Capture));
        _changes.EnsureCableFormat(fake);
        var disguised = new CableInfo(Dev(Spk, "CABLE Input (VB-Audio Virtual Cable)", DeviceFlow.Render), null);
        var r = _changes.EnsureCableFormat(disguised);
        Assert.AreEqual(ApplyOutcome.Skipped, r.Steps[0].Outcome);
        Assert.IsFalse(_formats.Writes.Contains(Spk));
        Assert.AreEqual(44100, _formats.RateOf(Spk));
        // Wrong direction: a playback name on the recording side.
        var swapped = new CableInfo(null, Dev(CIn, "CABLE Input (VB-Audio Virtual Cable)", DeviceFlow.Capture));
        Assert.AreEqual(ApplyOutcome.Skipped, _changes.EnsureCableFormat(swapped).Steps[1].Outcome);
    }

    [TestMethod]
    public void Undo_never_writes_to_a_device_that_is_no_longer_the_cable()
    {
        _changes.EnsureCableFormat(Cable);
        _formats.Writes.Clear();
        var d = _formats.Devices[CIn];
        _formats.Devices[CIn] = (d.Who with { Name = "Speakers" }, d.Formats);
        _changes.UndoAll();
        Assert.IsFalse(_formats.Writes.Contains(CIn));
    }

    [TestMethod]
    public void Already_48k_changes_nothing()
    {
        _formats.Add(CIn, "CABLE Input (VB-Audio Virtual Cable)", DeviceFlow.Render, 48000);
        _formats.Add(COut, "CABLE Output (VB-Audio Virtual Cable)", DeviceFlow.Capture, 48000);
        var r = _changes.EnsureCableFormat(Cable);
        Assert.IsFalse(r.Changed);
        Assert.AreEqual(0, _formats.Writes.Count);
        Assert.AreEqual(0, _journal.Read().Count);
    }

    [TestMethod]
    public void Undo_puts_the_old_bytes_back_and_is_idempotent()
    {
        var before = _formats.Devices[CIn].Formats;
        _changes.EnsureCableFormat(Cable);
        Assert.AreEqual(2, _changes.UndoAll().RestoredCount);
        CollectionAssert.AreEqual(before.Device, _formats.Devices[CIn].Formats.Device);
        CollectionAssert.AreEqual(before.Mix, _formats.Devices[CIn].Formats.Mix);
        int writes = _formats.Writes.Count;
        Assert.AreEqual(0, _changes.UndoAll().Steps.Count);
        Assert.AreEqual(writes, _formats.Writes.Count);
    }

    [TestMethod]
    public void The_cable_format_survives_exit_and_crash_handling()
    {
        _changes.EnsureCableFormat(Cable);
        _changes.UndoSession(Environment.ProcessId);
        _changes.UndoOrphanedSession(_ => false);
        Assert.AreEqual(48000, _formats.RateOf(CIn));
        Assert.AreEqual(2, _journal.Read().Count);
    }

    [TestMethod]
    public void A_rate_changed_by_hand_since_is_left_alone()
    {
        _changes.EnsureCableFormat(Cable);
        _formats.Add(CIn, "CABLE Input (VB-Audio Virtual Cable)", DeviceFlow.Render, 96000);
        var r = _changes.UndoAll();
        Assert.AreEqual(UndoOutcome.LeftAsIs, r.Steps.Single(s => s.Entry.DeviceId == CIn).Outcome);
        Assert.AreEqual(96000, _formats.RateOf(CIn));
    }

    [TestMethod]
    public void A_removed_cable_drops_its_entry()
    {
        _changes.EnsureCableFormat(Cable);
        _formats.Devices.Remove(CIn);
        _formats.Add(COut, "CABLE Output (VB-Audio Virtual Cable)", DeviceFlow.Capture, 48000, DeviceStatus.NotPresent);
        var r = _changes.UndoAll();
        Assert.IsTrue(r.Steps.All(s => s.Outcome == UndoOutcome.Gone));
        Assert.AreEqual(0, _journal.Read().Count);
    }

    [TestMethod]
    public void A_disabled_cable_keeps_its_entry_for_later()
    {
        _changes.EnsureCableFormat(Cable);
        var d = _formats.Devices[CIn];
        _formats.Devices[CIn] = (d.Who with { Status = DeviceStatus.Disabled }, d.Formats);
        var r = _changes.UndoAll();
        Assert.IsFalse(r.AllDone);
        Assert.IsTrue(_journal.Read().Any(e => e.DeviceId == CIn));
    }

    [TestMethod]
    public void A_refused_change_leaves_44k_and_no_entry()
    {
        _formats.Refuse = true;
        var r = _changes.EnsureCableFormat(Cable);
        Assert.IsTrue(r.Steps.All(s => s.Outcome == ApplyOutcome.Failed && s.Rate == 44100 && s.Error is not null));
        Assert.AreEqual(0, _journal.Read().Count);
    }

    [TestMethod]
    public void A_change_windows_does_not_take_counts_as_failed()
    {
        _formats.Ignore = true;
        var r = _changes.EnsureCableFormat(Cable);
        Assert.IsTrue(r.Steps.All(s => s.Outcome == ApplyOutcome.Failed));
        Assert.IsFalse(r.Changed);
        Assert.AreEqual(0, _journal.Read().Count);
    }

    [TestMethod]
    public void Lines_say_the_cable_format_and_the_rate_before()
    {
        _changes.EnsureCableFormat(Cable);
        var line = SystemChanges.Describe(_journal.Read().First(e => e.DeviceId == CIn));
        StringAssert.Contains(line, "48 kHz");
        StringAssert.Contains(line, "44,1 kHz");
    }
}
