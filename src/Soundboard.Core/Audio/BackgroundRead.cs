using System.Runtime.ExceptionServices;

namespace Soundboard.Core.Audio;

/// <summary>
/// A slow read (the audio device list takes over a second with many old endpoints) done off the
/// calling thread. The result comes back through <c>post</c> (the UI dispatcher) and only the
/// newest read counts: a read overtaken by a newer one or cancelled is dropped.
/// <see cref="Start"/>, <see cref="Cancel"/> and <see cref="IsReading"/> belong to the thread that
/// <c>post</c> leads back to.
/// </summary>
public sealed class BackgroundRead<T>(Func<T> read, Action<Action> post)
{
    private int _run;

    /// <summary>A read is on its way whose result will still be applied.</summary>
    public bool IsReading { get; private set; }

    /// <summary>
    /// Starts a read; <paramref name="apply"/> gets its result on the posting thread, unless a newer
    /// read or <see cref="Cancel"/> came in between. A failed read is thrown on the posting thread
    /// (after <paramref name="failed"/>, if given). The task ends once the result has been posted.
    /// </summary>
    public Task Start(Action<T> apply, Action? failed = null)
    {
        int run = ++_run;
        IsReading = true;
        return Task.Run(read).ContinueWith(t => post(() =>
        {
            if (run != _run) return;
            IsReading = false;
            if (t.Exception is { } ex)
            {
                failed?.Invoke();
                ExceptionDispatchInfo.Throw(ex.InnerExceptions.Count == 1 ? ex.InnerExceptions[0] : ex);
            }
            apply(t.Result);
        }), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>The read on its way (if any) is not applied.</summary>
    public void Cancel()
    {
        _run++;
        IsReading = false;
    }
}
