using System.Runtime.InteropServices;
using System.Windows.Interop;
using Soundboard.Core.Hotkeys;

namespace Soundboard.App.Hotkeys;

/// <summary>IHotkeyApi with a pressed event (the message loop side of RegisterHotKey).</summary>
public interface IHotkeyHost : IHotkeyApi, IDisposable
{
    event Action<int>? Pressed;
}

/// <summary>
/// RegisterHotKey on a hidden message-only window. Keys are caught globally (the game does
/// not see them while registered) — intended. MOD_NOREPEAT: holding a key fires once.
/// </summary>
public sealed class Win32HotkeyApi : IHotkeyHost
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;

    public Win32HotkeyApi()
    {
        var p = new HwndSourceParameters("SoundboardHotkeys") { ParentWindow = HwndMessage, WindowStyle = 0 };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public event Action<int>? Pressed;

    public HotkeyRegisterResult Register(int id, HotkeyGesture gesture)
    {
        if (RegisterHotKey(_source.Handle, id, (uint)gesture.Modifiers | ModNoRepeat, (uint)gesture.VirtualKey))
            return HotkeyRegisterResult.Ok;
        return Marshal.GetLastWin32Error() == ErrorHotkeyAlreadyRegistered
            ? HotkeyRegisterResult.TakenByOtherProgram
            : HotkeyRegisterResult.Failed;
    }

    public void Unregister(int id) => UnregisterHotKey(_source.Handle, id);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            Pressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>Registers nothing; for previews.</summary>
public sealed class NoHotkeyApi : IHotkeyHost
{
    public event Action<int>? Pressed { add { } remove { } }

    public HotkeyRegisterResult Register(int id, HotkeyGesture gesture) => HotkeyRegisterResult.Ok;

    public void Unregister(int id)
    {
    }

    public void Dispose()
    {
    }
}
