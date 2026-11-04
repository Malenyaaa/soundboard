using System.IO;
using Microsoft.Win32;
using Soundboard.Core.Text;

namespace Soundboard.App;

/// <summary>
/// One value in the per-user Run key. Reading never throws (an unreadable key counts as no
/// value); writing and deleting throw when Windows refuses (policy, rights).
/// </summary>
public interface IRunKey
{
    string? Read(string name);

    void Write(string name, string value);

    void Delete(string name);
}

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run – no admin rights needed.</summary>
public sealed class RegistryRunKey : IRunKey
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(name) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public void Write(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

public enum AutostartKind
{
    /// <summary>No entry.</summary>
    Off,

    /// <summary>The entry starts this program file.</summary>
    ThisCopy,

    /// <summary>The entry starts another program file (moved EXE, an old portable copy).</summary>
    OtherCopy,
}

public sealed record AutostartState(AutostartKind Kind, string? OtherPath);

/// <summary>
/// "Start with Windows (only in the notification area)": the Run value "Soundboard" with
/// <c>"&lt;exe&gt;" --tray</c>. Same value name as the installer's option, so both see and
/// change the same entry. Nothing is set without the user asking for it.
/// </summary>
public sealed class Autostart(IRunKey key, string? exePath)
{
    /// <summary>Also used by the installer script ([Registry], task "autostart").</summary>
    public const string ValueName = "Soundboard";

    public string? ExePath => exePath;

    public static string CommandFor(string exe) => "\"" + exe + "\" --tray";

    /// <summary>The program file in a Run value: quoted, or unquoted up to ".exe".</summary>
    public static string? ExeOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var c = command.Trim();
        if (c.StartsWith('"'))
        {
            int end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : c.Trim('"');
        }
        int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return c[..(exe + 4)];
        int space = c.IndexOf(' ');
        return space > 0 ? c[..space] : c;
    }

    public static bool SamePath(string? a, string? b)
    {
        if (a is null || b is null) return false;
        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path.Trim(); }
    }

    public AutostartState Read()
    {
        var value = key.Read(ValueName);
        if (string.IsNullOrWhiteSpace(value)) return new AutostartState(AutostartKind.Off, null);
        var exe = ExeOf(value);
        return SamePath(exe, exePath)
            ? new AutostartState(AutostartKind.ThisCopy, null)
            : new AutostartState(AutostartKind.OtherCopy, exe ?? value);
    }

    /// <summary>Sets the entry to this program file. Returns the reason when Windows refuses, else null.</summary>
    public string? Enable()
    {
        if (string.IsNullOrEmpty(exePath)) return L.Get("Autostart.NoPath");
        return Try(() => key.Write(ValueName, CommandFor(exePath)));
    }

    /// <summary>Removes the entry, whichever copy it starts. Returns the reason when Windows refuses, else null.</summary>
    public string? Disable() => Try(() => key.Delete(ValueName));

    /// <summary>
    /// Removal guide: only an entry that starts this program file is removed; one for another
    /// copy belongs to that copy. False when there was nothing of ours to remove.
    /// </summary>
    public bool RemoveIfThisCopy(out string? error)
    {
        error = null;
        if (Read().Kind != AutostartKind.ThisCopy) return false;
        error = Disable();
        return true;
    }

    private static string? Try(Action write)
    {
        try
        {
            write();
            return null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return ex.Message;
        }
    }
}
