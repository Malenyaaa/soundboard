using System.Globalization;

namespace Soundboard.Core.Hotkeys;

/// <summary>Modifier keys, same bit values as the Win32 MOD_* constants.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A global key combination: modifiers + one Windows virtual-key code.
/// Stored as text like "Ctrl+Alt+NumPad1" (stable English tokens); shown in the language of the interface.
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    public const int VkPause = 0x13;
    public const int VkNumPad0 = 0x60;

    /// <summary>Fixed hotkey for "stop all".</summary>
    public static readonly HotkeyGesture StopAllDefault = new(HotkeyModifiers.None, VkPause);

    private static readonly Dictionary<int, (string Token, string Display)> Names = BuildNames();

    /// <summary>Text for the user, e.g. "Ctrl+Alt+Num 1" (English) or "Strg+Alt+Num 1" (German).</summary>
    public string Display
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add(Text.L.Get("Key.Ctrl"));
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add(Text.L.Get("Key.Alt"));
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add(Text.L.Get("Key.Shift"));
            if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add(Text.L.Get("Key.Win"));
            parts.Add(Names.TryGetValue(VirtualKey, out var n) ? Shown(n.Display) : Text.L.F("Key.Unknown", VirtualKey.ToString("X2", CultureInfo.InvariantCulture)));
            return string.Join("+", parts);
        }
    }

    /// <summary>Canonical text for the profile file.</summary>
    public string ToToken()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Names.TryGetValue(VirtualKey, out var n) ? n.Token : "VK" + VirtualKey.ToString("X2", CultureInfo.InvariantCulture));
        return string.Join("+", parts);
    }

    /// <summary>Key names that differ between the languages are stored as "@" plus their text key, e.g. "@Key.Delete".</summary>
    private static string Shown(string display) => display.StartsWith('@') ? Text.L.Get(display[1..]) : display;

    public override string ToString() => ToToken();

    /// <summary>Parses the canonical token form. Returns null for empty or unreadable text.</summary>
    public static HotkeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        var mods = HotkeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": mods |= HotkeyModifiers.Ctrl; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win": mods |= HotkeyModifiers.Win; break;
                default: return null;
            }
        }
        var key = parts[^1];
        foreach (var (vk, n) in Names)
        {
            if (string.Equals(n.Token, key, StringComparison.OrdinalIgnoreCase)) return new HotkeyGesture(mods, vk);
        }
        if (key.StartsWith("VK", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)
            && code is > 0 and < 256)
        {
            return new HotkeyGesture(mods, code);
        }
        return null;
    }

    /// <summary>Keys that cannot be a hotkey on their own (modifiers, Enter, Esc, ...).</summary>
    public static bool IsUsableKey(int vk) => vk switch
    {
        0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 => false, // modifiers
        0x0D or 0x1B or 0x09 or 0x08 or 0x14 or 0x90 => false, // Enter, Esc, Tab, Backspace, CapsLock, NumLock
        _ => vk is > 0 and < 256,
    };

    /// <summary>Numpad keys in the order they are suggested: Num 1 … Num 9, Num 0.</summary>
    public static IEnumerable<HotkeyGesture> NumpadSuggestions()
    {
        for (int i = 1; i <= 9; i++) yield return new HotkeyGesture(HotkeyModifiers.None, VkNumPad0 + i);
        yield return new HotkeyGesture(HotkeyModifiers.None, VkNumPad0);
        for (int i = 1; i <= 9; i++) yield return new HotkeyGesture(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, VkNumPad0 + i);
    }

    private static Dictionary<int, (string, string)> BuildNames()
    {
        var d = new Dictionary<int, (string, string)>();
        for (int i = 0; i <= 9; i++)
        {
            d[0x60 + i] = ($"NumPad{i}", $"Num {i}");
            d[0x30 + i] = ($"D{i}", i.ToString(CultureInfo.InvariantCulture));
        }
        for (char c = 'A'; c <= 'Z'; c++) d[c] = (c.ToString(), c.ToString());
        for (int i = 1; i <= 24; i++) d[0x6F + i] = ($"F{i}", $"F{i}");
        d[0x6A] = ("Multiply", "Num *");
        d[0x6B] = ("Add", "Num +");
        d[0x6D] = ("Subtract", "Num −");
        d[0x6E] = ("Decimal", "@Key.Decimal");
        d[0x6F] = ("Divide", "Num /");
        d[VkPause] = ("Pause", "Pause");
        d[0x91] = ("Scroll", "@Key.Scroll");
        d[0x2D] = ("Insert", "@Key.Insert");
        d[0x2E] = ("Delete", "@Key.Delete");
        d[0x24] = ("Home", "@Key.Home");
        d[0x23] = ("End", "@Key.End");
        d[0x21] = ("PageUp", "@Key.PageUp");
        d[0x22] = ("PageDown", "@Key.PageDown");
        d[0x20] = ("Space", "@Key.Space");
        d[0x25] = ("Left", "@Key.Left");
        d[0x26] = ("Up", "@Key.Up");
        d[0x27] = ("Right", "@Key.Right");
        d[0x28] = ("Down", "@Key.Down");
        d[0x2C] = ("PrintScreen", "@Key.PrintScreen");
        return d;
    }
}
