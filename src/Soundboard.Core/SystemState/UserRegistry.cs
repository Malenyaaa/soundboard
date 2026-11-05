using Microsoft.Win32;

namespace Soundboard.Core.SystemState;

/// <summary>A DWORD value as read: missing, a number, or something else (another type).</summary>
public readonly record struct DwordRead(bool Exists, int? Value)
{
    public static DwordRead Missing => new(false, null);

    public static DwordRead Of(int value) => new(true, value);

    /// <summary>The value exists but is not a DWORD: the app leaves it alone.</summary>
    public static DwordRead OtherType => new(true, null);
}

/// <summary>DWORD values under HKEY_CURRENT_USER (no admin rights). Replaceable in tests and previews.</summary>
public interface IUserRegistry
{
    /// <summary>Never throws for a missing key or value.</summary>
    DwordRead ReadDword(string subKey, string name);

    /// <summary>Throws when Windows refuses.</summary>
    void WriteDword(string subKey, string name, int value);

    /// <summary>Removes the value; a missing value is fine. Throws when Windows refuses.</summary>
    void DeleteValue(string subKey, string name);
}

/// <summary>The real per-user registry.</summary>
public sealed class UserRegistry : IUserRegistry
{
    public DwordRead ReadDword(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        if (key is null) return DwordRead.Missing;
        var v = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (v is null) return DwordRead.Missing;
        return key.GetValueKind(name) == RegistryValueKind.DWord && v is int i ? DwordRead.Of(i) : DwordRead.OtherType;
    }

    public void WriteDword(string subKey, string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void DeleteValue(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
