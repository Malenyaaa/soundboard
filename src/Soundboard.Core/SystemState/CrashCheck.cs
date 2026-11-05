using Soundboard.Core.Audio;

namespace Soundboard.Core.SystemState;

/// <summary>What the watcher found after the app process ended.</summary>
public sealed record CrashReport(bool CableWasActive, SnapshotComparison? Comparison, IReadOnlyList<AudioProblem> Problems)
{
    /// <summary>"Restore default device" can help: something does not work and a default differs from the saved one.</summary>
    public bool CanRestore => Problems.Count > 0 && Comparison?.DefaultsChanged == true;

    public string Headline => Text.L.Get("Crash.Headline");

    /// <summary>Explanation lines for the small watcher window.</summary>
    public IReadOnlyList<string> Lines(string discordWayBack)
    {
        var lines = new List<string>();
        if (CableWasActive)
        {
            lines.Add(Text.L.Get("Crash.CableIdle"));
            lines.Add(Text.L.F("Crash.Restart", discordWayBack));
        }
        if (Problems.Count > 0)
        {
            lines.Add(Text.L.Get("Crash.AudioProblem"));
            lines.AddRange(Problems.Select(p => "  " + p.Text));
        }
        return lines;
    }
}

/// <summary>
/// Decision of the watcher process (<c>--watch &lt;pid&gt;</c>): after the app ended, was it a
/// normal exit (running flag deleted) or not (flag still there) — and if not, is there anything
/// that does not work? Only then the window appears.
/// </summary>
public static class CrashCheck
{
    /// <summary>
    /// Null after a normal exit, and also after a crash that left everything working (no cable
    /// running, Windows audio fine); otherwise what to tell the user. <paramref name="readState"/>
    /// is only called after a crash.
    /// </summary>
    public static CrashReport? Evaluate(DirtyInfo? flag, int watchedPid, AudioSnapshot? snapshot, Func<AudioStateReport> readState)
    {
        if (flag is null) return null; // normal exit: the app removed its flag
        // A flag of another (newer) instance is not ours to report.
        if (flag.ProcessId != 0 && flag.ProcessId != watchedPid) return null;
        var now = readState();
        var problems = AudioHealth.Check(now, snapshot);
        if (!flag.CableActive && problems.Count == 0) return null; // nothing stopped working: stay silent
        var comparison = snapshot is null ? null : SnapshotService.Compare(snapshot, now);
        return new CrashReport(flag.CableActive, comparison, problems);
    }
}
