namespace Soundboard.Core.Text;

/// <summary>
/// Keeps notifications from piling up: each reason speaks at most once per interval. A key that is
/// pressed ten times in a row without effect gives one message, not ten.
/// </summary>
public sealed class HintThrottle(TimeSpan interval)
{
    private readonly Dictionary<string, DateTime> _last = [];

    public TimeSpan Interval { get; } = interval;

    /// <summary>True (and remembered) if <paramref name="reason"/> has not spoken within the interval.</summary>
    public bool ShouldShow(string reason, DateTime now)
    {
        if (_last.TryGetValue(reason, out var last) && now - last < Interval && now >= last) return false;
        _last[reason] = now;
        return true;
    }
}
