using System.Collections.Concurrent;
using Soundboard.Core.Audio;

namespace Soundboard.Core.Tests;

[TestClass]
public class BackgroundReadTests
{
    /// <summary>Stands for the UI dispatcher: posted work waits until the test runs it.</summary>
    private sealed class Posted
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        public void Post(Action a) => _queue.Enqueue(a);
        public int Count => _queue.Count;

        public void RunAll()
        {
            while (_queue.TryDequeue(out var a)) a();
        }
    }

    [TestMethod]
    public async Task The_read_runs_elsewhere_and_its_result_comes_back_through_post()
    {
        int caller = Environment.CurrentManagedThreadId, readOn = -1;
        var posted = new Posted();
        var reader = new BackgroundRead<int>(() => { readOn = Environment.CurrentManagedThreadId; return 42; }, posted.Post);
        int got = 0;

        var started = reader.Start(v => got = v);
        Assert.IsTrue(reader.IsReading);
        await started;
        Assert.AreNotEqual(caller, readOn);
        Assert.AreEqual(0, got, "nothing is applied before the posting thread runs it");

        posted.RunAll();
        Assert.AreEqual(42, got);
        Assert.IsFalse(reader.IsReading);
    }

    [TestMethod]
    public async Task Start_returns_before_a_slow_read_ends()
    {
        using var release = new ManualResetEventSlim();
        var posted = new Posted();
        var reader = new BackgroundRead<string>(() => { release.Wait(TimeSpan.FromSeconds(10)); return "list"; }, posted.Post);
        string? got = null;

        var started = reader.Start(v => got = v);
        Assert.IsFalse(started.IsCompleted, "the caller is not held up by the read");
        Assert.IsTrue(reader.IsReading);

        release.Set();
        await started;
        posted.RunAll();
        Assert.AreEqual("list", got);
    }

    [TestMethod]
    public async Task Only_the_newest_read_is_applied()
    {
        using var firstMayEnd = new ManualResetEventSlim();
        using var firstEntered = new ManualResetEventSlim();
        int calls = 0;
        var posted = new Posted();
        var reader = new BackgroundRead<int>(() =>
        {
            int n = Interlocked.Increment(ref calls);
            if (n == 1)
            {
                // The older read is the slower one: its result arrives after the newer one's.
                firstEntered.Set();
                firstMayEnd.Wait(TimeSpan.FromSeconds(10));
            }
            return n;
        }, posted.Post);
        var applied = new List<int>();

        var first = reader.Start(applied.Add);
        Assert.IsTrue(firstEntered.Wait(TimeSpan.FromSeconds(10)));
        var second = reader.Start(applied.Add);
        await second;
        firstMayEnd.Set();
        await first;
        posted.RunAll();

        CollectionAssert.AreEqual(new[] { 2 }, applied);
        Assert.IsFalse(reader.IsReading);
    }

    [TestMethod]
    public async Task A_cancelled_read_is_dropped()
    {
        var posted = new Posted();
        var reader = new BackgroundRead<int>(() => 1, posted.Post);
        bool applied = false;

        var started = reader.Start(_ => applied = true);
        reader.Cancel();
        Assert.IsFalse(reader.IsReading);
        await started;
        posted.RunAll();
        Assert.IsFalse(applied);
    }

    [TestMethod]
    public async Task A_failed_read_is_thrown_on_the_posting_thread()
    {
        var posted = new Posted();
        var reader = new BackgroundRead<int>(() => throw new InvalidOperationException("audio service gone"), posted.Post);
        bool failed = false, applied = false;

        await reader.Start(_ => applied = true, failed: () => failed = true);
        Assert.AreEqual(1, posted.Count);
        var ex = Assert.ThrowsException<InvalidOperationException>(posted.RunAll);
        Assert.AreEqual("audio service gone", ex.Message);
        Assert.IsTrue(failed);
        Assert.IsFalse(applied);
        Assert.IsFalse(reader.IsReading);
    }
}
