namespace Soundboard.Core.SystemState;

/// <summary>What the watcher found after the app process ended.</summary>
public sealed record CrashReport(bool CableWasActive, SnapshotComparison? Comparison)
{
    public bool DefaultsChanged => Comparison?.DefaultsChanged == true;

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
        if (Comparison is null)
            lines.Add(Text.L.Get("Crash.NoSnapshot"));
        else if (!Comparison.DefaultsChanged)
            lines.Add(Text.L.Get("Crash.DefaultsSame"));
        else
        {
            lines.Add(Text.L.Get("Crash.DefaultsChanged"));
            lines.AddRange(Comparison.DefaultChangeLines.Select(l => "  " + l));
        }
        return lines;
    }
}

/// <summary>
/// Decision of the watcher process (<c>--watch &lt;pid&gt;</c>): after the app ended, was it a
/// normal exit (running flag deleted) or not (flag still there)?
/// </summary>
public static class CrashCheck
{
    /// <summary>Null after a normal exit; otherwise what to tell the user.</summary>
    public static CrashReport? Evaluate(DirtyInfo? flag, int watchedPid, Func<SnapshotComparison?> compare)
    {
        if (flag is null) return null; // normal exit: the app removed its flag
        // A flag of another (newer) instance is not ours to report.
        if (flag.ProcessId != 0 && flag.ProcessId != watchedPid) return null;
        return new CrashReport(flag.CableActive, compare());
    }
}
