namespace Soundboard.Core.SystemState;

/// <summary>What the watcher tells the user after the app ended unexpectedly while the cable ran.</summary>
public sealed record CrashReport(bool CableWasActive, int RestoredCount)
{
    public string Headline => Text.L.Get("Crash.Headline");

    /// <summary>Explanation lines for the small watcher window.</summary>
    public IReadOnlyList<string> Lines(string discordWayBack)
    {
        var lines = new List<string>
        {
            Text.L.Get("Crash.CableIdle"),
            Text.L.F("Crash.Restart", discordWayBack),
        };
        if (RestoredCount > 0) lines.Add(Text.L.Get("Crash.Restored"));
        return lines;
    }
}

/// <summary>
/// Decision of the watcher process (<c>--watch &lt;pid&gt;</c>) after the app ended. Putting back
/// what the app changed in Windows happens before, always and silently (see
/// <see cref="SystemChanges.UndoSession"/>). A window is only worth it when Discord's microphone is
/// silent now: the app did not end normally (running flag still there) and the cable was running.
/// </summary>
public static class CrashCheck
{
    /// <summary>Null when the watcher stays silent; otherwise what the window says.</summary>
    public static CrashReport? Evaluate(DirtyInfo? flag, int watchedPid, UndoResult undone)
    {
        if (flag is null) return null; // normal exit: the app removed its flag
        // A flag of another (newer) instance is not ours to report.
        if (flag.ProcessId != 0 && flag.ProcessId != watchedPid) return null;
        if (!flag.CableActive) return null; // Discord does not miss anything: stay silent
        return new CrashReport(true, undone.RestoredCount);
    }
}
