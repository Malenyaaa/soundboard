using Soundboard.Core.SystemState;

namespace Soundboard.Core.Tests;

/// <summary>Registry in memory: the tests never touch the real Windows registry.</summary>
internal sealed class FakeRegistry : IUserRegistry
{
    private readonly Dictionary<string, object> _values = [];

    /// <summary>Called before every write or delete (e.g. to look at the journal at that moment).</summary>
    public Action? BeforeWrite { get; set; }

    public bool RefuseWrites { get; set; }

    public int Writes { get; private set; }

    public void Set(int value) => _values[SystemChanges.DuckingValueName] = value;

    public void SetText(string value) => _values[SystemChanges.DuckingValueName] = value;

    public object? Value => _values.GetValueOrDefault(SystemChanges.DuckingValueName);

    public DwordRead ReadDword(string subKey, string name) => _values.TryGetValue(name, out var v)
        ? v is int i ? DwordRead.Of(i) : DwordRead.OtherType
        : DwordRead.Missing;

    public void WriteDword(string subKey, string name, int value)
    {
        Assert.AreEqual(SystemChanges.AudioSubKey, subKey);
        BeforeWrite?.Invoke();
        if (RefuseWrites) throw new UnauthorizedAccessException("refused");
        Writes++;
        _values[name] = value;
    }

    public void DeleteValue(string subKey, string name)
    {
        BeforeWrite?.Invoke();
        if (RefuseWrites) throw new UnauthorizedAccessException("refused");
        Writes++;
        _values.Remove(name);
    }
}

[TestClass]
public sealed class SystemChangesTests
{
    private string _dir = "";
    private ChangeJournal _journal = null!;
    private FakeRegistry _reg = null!;
    private SystemChanges _changes = null!;
    private static readonly ChangeOwner Me = new(4242, new DateTime(2026, 11, 5, 9, 0, 0));

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sb-changes-" + Guid.NewGuid().ToString("N"));
        _journal = new ChangeJournal(Path.Combine(_dir, "changes.json"));
        _reg = new FakeRegistry();
        _changes = new SystemChanges(_journal, _reg);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { }
    }

    [TestMethod]
    public void The_old_value_is_on_disk_before_windows_is_changed()
    {
        _reg.Set(1);
        bool checkedAhead = false;
        _reg.BeforeWrite = () =>
        {
            // A second journal object on the same file sees the entry already (same thread: this checks the
            // order of writing, not the lock between processes – that has its own test).
            var e = new ChangeJournal(_journal.Path).Read().Single();
            Assert.AreEqual(1, e.OldValue);
            Assert.IsFalse(e.Applied);
            checkedAhead = true;
        };
        Assert.AreEqual(ApplyOutcome.Changed, _changes.ApplyDucking(Me).Outcome);
        Assert.IsTrue(checkedAhead);
        Assert.AreEqual(3, _reg.Value);
        var saved = _journal.Read().Single();
        Assert.IsTrue(saved.Applied);
        Assert.AreEqual(ChangeScope.Session, saved.Scope);
        Assert.AreEqual(Me, saved.Owner);
    }

    [TestMethod]
    public void Undo_writes_the_old_value_back_and_removes_the_entry()
    {
        _reg.Set(2);
        _changes.ApplyDucking(Me);
        var r = _changes.UndoSession(Me);
        Assert.AreEqual(1, r.RestoredCount);
        Assert.AreEqual(2, _reg.Value);
        Assert.AreEqual(0, _journal.Read().Count);
        Assert.IsFalse(File.Exists(_journal.Path), "an empty journal is no file");
    }

    [TestMethod]
    public void A_value_that_was_missing_is_deleted_again_not_set_to_the_default()
    {
        _changes.ApplyDucking(Me);
        Assert.AreEqual(3, _reg.Value);
        Assert.IsTrue(_journal.Read().Single().OldMissing);
        _changes.UndoAll();
        Assert.IsNull(_reg.Value, "the value must be gone, not 1");
    }

    [TestMethod]
    public void Undo_is_idempotent()
    {
        _reg.Set(1);
        _changes.ApplyDucking(Me);
        _changes.UndoAll();
        int writes = _reg.Writes;
        var again = _changes.UndoAll();
        Assert.AreEqual(0, again.Steps.Count);
        Assert.AreEqual(writes, _reg.Writes, "nothing written a second time");
        Assert.AreEqual(1, _reg.Value);
    }

    [TestMethod]
    public void A_value_changed_by_someone_since_is_left_alone()
    {
        _reg.Set(1);
        _changes.ApplyDucking(Me);
        _reg.Set(0); // the user chose "mute all other sounds" in Windows meanwhile
        var r = _changes.UndoAll();
        Assert.AreEqual(UndoOutcome.LeftAsIs, r.Steps.Single().Outcome);
        Assert.AreEqual(0, _reg.Value);
        Assert.AreEqual(0, _journal.Read().Count);
    }

    [TestMethod]
    public void Already_do_nothing_means_no_change_and_no_entry()
    {
        _reg.Set(3);
        Assert.AreEqual(ApplyOutcome.AlreadySet, _changes.ApplyDucking(Me).Outcome);
        Assert.AreEqual(0, _journal.Read().Count);
        Assert.AreEqual(0, _reg.Writes);
    }

    [TestMethod]
    public void A_value_of_another_type_is_not_touched()
    {
        _reg.SetText("odd");
        Assert.AreEqual(ApplyOutcome.Skipped, _changes.ApplyDucking(Me).Outcome);
        Assert.AreEqual("odd", _reg.Value);
        Assert.AreEqual(0, _journal.Read().Count);
    }

    [TestMethod]
    public void A_refused_change_leaves_no_entry_and_the_old_value()
    {
        _reg.Set(1);
        _reg.RefuseWrites = true;
        var (outcome, error) = _changes.ApplyDucking(Me);
        Assert.AreEqual(ApplyOutcome.Failed, outcome);
        Assert.IsNotNull(error);
        Assert.AreEqual(1, _reg.Value);
        Assert.AreEqual(0, _journal.Read().Count);
    }

    [TestMethod]
    public void Crash_between_journal_and_change_is_harmless()
    {
        // Entry written ahead, then the process died before the registry write.
        _reg.Set(1);
        _journal.Put(new ChangeEntry { Key = SystemChanges.DuckingKey, Kind = ChangeKind.Ducking, Scope = ChangeScope.Session, Owner = Me, OldValue = 1, NewValue = 3, Applied = false });
        var r = _changes.UndoSession(Me);
        Assert.AreEqual(UndoOutcome.LeftAsIs, r.Steps.Single().Outcome);
        Assert.AreEqual(1, _reg.Value);
        Assert.AreEqual(0, _reg.Writes);
        Assert.AreEqual(0, _journal.Read().Count);
    }

    [TestMethod]
    public void An_entry_left_by_a_crash_keeps_its_old_value_when_the_next_run_takes_over()
    {
        _reg.Set(1);
        var crashed = new ChangeOwner(1111, new DateTime(2026, 11, 4));
        _changes.ApplyDucking(crashed);
        Assert.AreEqual(3, _reg.Value);
        // The next run starts while the 3 is still set: the 3 must never become "the value before".
        Assert.AreEqual(ApplyOutcome.AlreadyOurs, _changes.ApplyDucking(Me).Outcome);
        var e = _journal.Read().Single();
        Assert.AreEqual(1, e.OldValue);
        Assert.AreEqual(Me, e.Owner);
        _changes.UndoSession(Me);
        Assert.AreEqual(1, _reg.Value);
    }

    [TestMethod]
    public void Session_undo_takes_only_the_given_run()
    {
        _reg.Set(1);
        _changes.ApplyDucking(Me);
        Assert.AreEqual(0, _changes.UndoSession(Me with { ProcessId = 9999 }).Steps.Count);
        Assert.AreEqual(3, _reg.Value);
    }

    [TestMethod]
    public void At_start_only_changes_of_runs_that_are_gone_go_back()
    {
        _reg.Set(1);
        _changes.ApplyDucking(Me);
        Assert.AreEqual(0, _changes.UndoOrphanedSession(_ => true).Steps.Count, "owner still runs");
        Assert.AreEqual(3, _reg.Value);
        Assert.AreEqual(1, _changes.UndoOrphanedSession(_ => false).RestoredCount);
        Assert.AreEqual(1, _reg.Value);
    }

    [TestMethod]
    public void A_refused_undo_keeps_the_entry_for_the_next_try()
    {
        _reg.Set(1);
        _changes.ApplyDucking(Me);
        _reg.RefuseWrites = true;
        var r = _changes.UndoAll();
        Assert.IsFalse(r.AllDone);
        Assert.AreEqual(1, _journal.Read().Count);
        _reg.RefuseWrites = false;
        Assert.AreEqual(1, _changes.UndoAll().RestoredCount);
        Assert.AreEqual(1, _reg.Value);
    }

    [TestMethod]
    public void An_unreadable_journal_is_set_aside_not_overwritten()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_journal.Path, "{ broken");
        Assert.AreEqual(0, _journal.Read().Count);
        Assert.IsNotNull(_journal.SetAsideFile);
        Assert.AreEqual("{ broken", File.ReadAllText(_journal.SetAsideFile!));
    }

    [TestMethod]
    public void Crash_after_the_change_but_before_marking_it_is_put_back()
    {
        // Entry written ahead, registry changed, then the process died before "applied" was saved.
        _reg.Set(1);
        _reg.Set(3);
        _journal.Put(new ChangeEntry { Key = SystemChanges.DuckingKey, Kind = ChangeKind.Ducking, Scope = ChangeScope.Session, Owner = Me, OldValue = 1, NewValue = 3, Applied = false });
        Assert.AreEqual(1, _changes.UndoOrphanedSession(_ => false).RestoredCount);
        Assert.AreEqual(1, _reg.Value);
    }

    [TestMethod]
    public void Same_process_id_with_another_start_time_is_another_run()
    {
        _reg.Set(1);
        _changes.ApplyDucking(Me);
        Assert.AreEqual(0, _changes.UndoSession(Me with { StartedAt = Me.StartedAt.AddHours(1) }).Steps.Count);
        Assert.AreEqual(3, _reg.Value);
    }

    [TestMethod]
    public void A_journal_held_by_another_process_is_not_touched_without_the_lock()
    {
        _reg.Set(1);
        _changes.ApplyDucking(Me);
        _journal.LockTimeout = TimeSpan.FromMilliseconds(100);
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var other = new Thread(() =>
        {
            using var m = new Mutex(false, _journal.MutexName);
            m.WaitOne();
            held.Set();
            release.Wait();
            m.ReleaseMutex();
        });
        other.Start();
        held.Wait();
        try
        {
            var r = _changes.UndoAll();
            Assert.IsFalse(r.AllDone);
            Assert.AreEqual(3, _reg.Value, "nothing changed without the lock");
        }
        finally
        {
            release.Set();
            other.Join();
        }
        Assert.AreEqual(1, _journal.Read().Count, "the entry is still there");
        Assert.AreEqual(1, _changes.UndoAll().RestoredCount);
    }

    [TestMethod]
    public void Lines_name_the_setting_and_the_value_before()
    {
        _changes.ApplyDucking(Me);
        var line = SystemChanges.Describe(_journal.Read().Single());
        StringAssert.Contains(line, "volle Lautstärke");
        StringAssert.Contains(line, "um 80 % leiser");
    }
}
