using System.Threading;

namespace Soundboard.App;

/// <summary>
/// One running copy per user session. A second start only asks the first one to show its
/// window and ends. The mutex name is also used by the installer to detect a running copy.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const string MutexName = "Soundboard.SingleInstance";
    private const string ShowEventName = "Soundboard.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle show)
    {
        _mutex = mutex;
        _show = show;
    }

    /// <summary>Null if another copy already runs (it was asked to show itself).</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(true, MutexName, out bool created);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (created) return new SingleInstance(mutex, show);
        show.Set();
        show.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>True if a copy is running (used by --uninstall).</summary>
    public static bool IsRunning()
    {
        if (Mutex.TryOpenExisting(MutexName, out var m))
        {
            m.Dispose();
            return true;
        }
        return false;
    }

    /// <summary>Calls <paramref name="onShow"/> (on a worker thread) whenever a second start asks.</summary>
    public void ListenForShow(Action onShow) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _wait?.Unregister(null);
        _show.Dispose();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }
}
