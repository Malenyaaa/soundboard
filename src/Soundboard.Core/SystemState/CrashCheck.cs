namespace Soundboard.Core.SystemState;

/// <summary>What the watcher found after the app process ended.</summary>
public sealed record CrashReport(bool CableWasActive, SnapshotComparison? Comparison)
{
    public bool DefaultsChanged => Comparison?.DefaultsChanged == true;

    public string Headline => "Das Soundboard wurde unerwartet beendet.";

    /// <summary>German explanation lines for the small watcher window.</summary>
    public IReadOnlyList<string> Lines(string discordWayBack)
    {
        var lines = new List<string>();
        if (CableWasActive)
        {
            lines.Add("Discord hört dein Kabel, aber niemand füttert es – dein Mikrofon ist in Discord gerade stumm.");
            lines.Add("Starte das Soundboard neu, oder: " + discordWayBack);
        }
        if (Comparison is null)
            lines.Add("Eine Sicherung des Audio-Zustands gibt es nicht; verglichen wurde nichts.");
        else if (!Comparison.DefaultsChanged)
            lines.Add("Die Windows-Standardgeräte sind wie gesichert.");
        else
        {
            lines.Add("Ein Windows-Standardgerät weicht von der Sicherung ab (das Soundboard hat es nicht verstellt):");
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
