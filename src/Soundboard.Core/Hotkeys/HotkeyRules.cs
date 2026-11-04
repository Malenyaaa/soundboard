namespace Soundboard.Core.Hotkeys;

/// <summary>
/// Which keys may be given to whom. Function keys ("stop all", "mute microphone") must be
/// unique: they never share a key with a pad or with each other. Two pads may share a key for
/// a moment (the first one wins, the second shows "move here"), so a pad only clashes with a
/// function key.
/// </summary>
public static class HotkeyRules
{
    /// <summary>
    /// The owner that stands in the way of giving <paramref name="gesture"/> to <paramref name="owner"/>,
    /// or null if the key may be taken.
    /// </summary>
    /// <param name="all">Every binding (owner and key) as it is now.</param>
    /// <param name="isFunction">Tells whether an owner is a function key.</param>
    public static string? Conflict(HotkeyGesture gesture, string owner,
        IEnumerable<(string Owner, HotkeyGesture? Gesture)> all, Func<string, bool> isFunction)
    {
        bool mineIsFunction = isFunction(owner);
        foreach (var (other, g) in all)
        {
            if (other == owner || g != gesture) continue;
            if (mineIsFunction || isFunction(other)) return other;
        }
        return null;
    }
}
