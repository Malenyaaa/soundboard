using System.IO;
using Microsoft.Win32;

namespace Soundboard.App;

/// <summary>
/// The optional "start with Windows" entry (HKCU\...\Run, value "Soundboard"). The app never
/// sets it by itself; the installer does so only if its checkbox is ticked. Here it is only
/// read and, in the removal guide, deleted.
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "Soundboard";

    public static string? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Removes the entry. Returns an error text or null.</summary>
    public static string? Remove()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return ex.Message;
        }
    }
}
