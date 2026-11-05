using Soundboard.Core.SystemState;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class CrashCheckTests
{
    private static readonly UndoResult Nothing = UndoResult.Empty;

    private static UndoResult OneRestored => new()
    {
        Steps = [new UndoStep(new ChangeEntry { Key = SystemChanges.DuckingKey, Kind = ChangeKind.Ducking, NewValue = 3 }, UndoOutcome.Restored, null)],
    };

    [TestMethod]
    public void Normal_exit_shows_no_window()
    {
        Assert.IsNull(CrashCheck.Evaluate(null, 42, OneRestored));
    }

    [TestMethod]
    public void Flag_of_another_instance_is_ignored()
    {
        Assert.IsNull(CrashCheck.Evaluate(new DirtyInfo(7, DateTime.Now, true), 42, Nothing));
    }

    [TestMethod]
    public void Crash_with_cable_explains_discord_silence_and_way_back()
    {
        var r = CrashCheck.Evaluate(new DirtyInfo(42, DateTime.Now, true), 42, Nothing)!;
        var text = string.Join("\n", r.Lines("In Discord wieder „Headset-Mikro“ wählen."));
        StringAssert.Contains(text, "niemand füttert es");
        StringAssert.Contains(text, "Headset-Mikro");
        Assert.IsFalse(text.Contains("zurückgestellt"), "nothing was put back, so nothing is said about it");
    }

    [TestMethod]
    public void Crash_with_cable_mentions_what_was_put_back()
    {
        var r = CrashCheck.Evaluate(new DirtyInfo(42, DateTime.Now, true), 42, OneRestored)!;
        StringAssert.Contains(string.Join("\n", r.Lines("x")), "schon zurückgestellt");
    }

    [TestMethod]
    public void Crash_without_cable_stays_silent_even_after_putting_things_back()
    {
        Assert.IsNull(CrashCheck.Evaluate(new DirtyInfo(42, DateTime.Now, false), 42, OneRestored));
    }

    [TestMethod]
    public void Unreadable_flag_says_nothing_without_cable()
    {
        Assert.IsNull(CrashCheck.Evaluate(new DirtyInfo(0, DateTime.MinValue, false), 42, Nothing));
        Assert.IsNotNull(CrashCheck.Evaluate(new DirtyInfo(0, DateTime.MinValue, true), 42, Nothing));
    }

    /// <summary>The watcher's whole job after a crash, with the journal and a fake registry.</summary>
    [TestMethod]
    public void Watcher_puts_back_the_crashed_runs_ducking_silently_and_leaves_the_cable_format()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sb-watch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new ChangeJournal(Path.Combine(dir, "changes.json"));
            var reg = new FakeRegistry();
            var formats = new FakeFormats();
            formats.Add("cin", "CABLE Input (VB-Audio Virtual Cable)", Audio.DeviceFlow.Render, 44100);
            var app = new SystemChanges(journal, reg, formats);
            var crashed = new ChangeOwner(42, DateTime.Now);
            app.ApplyDucking(crashed);
            app.EnsureCableFormat(new Audio.CableInfo(
                new Audio.AudioDeviceInfo("cin", "CABLE Input (VB-Audio Virtual Cable)", Audio.DeviceFlow.Render, Audio.DeviceStatus.Active, [], 1f, false, null), null));
            // Another run is alive and has its own (none here); the crashed one was 42.
            var watcher = new SystemChanges(new ChangeJournal(journal.Path), reg, formats);
            Assert.AreEqual(0, watcher.UndoSession(crashed with { StartedAt = crashed.StartedAt.AddMinutes(-5) }).Steps.Count, "same ID, other start: another run");
            var undone = watcher.UndoSession(crashed);
            Assert.AreEqual(1, undone.RestoredCount);
            Assert.IsNull(reg.Value, "ducking value deleted again (it was missing before)");
            Assert.AreEqual(48000, formats.RateOf("cin"), "the cable format stays until removal");
            Assert.IsNull(CrashCheck.Evaluate(new DirtyInfo(42, DateTime.Now, false), 42, undone), "no cable: no window");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (DirectoryNotFoundException) { }
        }
    }
}
