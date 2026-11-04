using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Soundboard.App.ViewModels;
using Soundboard.Core.Audio;
using Soundboard.Core.Config;
using Soundboard.Core.SystemState;

namespace Soundboard.App;

/// <summary>
/// Watcher child process (<c>Soundboard.exe --watch &lt;pid&gt;</c>). Waits until the app
/// ends. Normal exit (running flag removed): ends silently. Otherwise: compares the saved
/// audio state with Windows (read only) and shows a small window. No service, no admin.
/// </summary>
internal static class Watcher
{
    /// <summary>Starts the watcher for this process. Failure only means no crash notice.</summary>
    public static void StartFor(int pid)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        try
        {
            var psi = new ProcessStartInfo(exe, $"--watch {pid}") { UseShellExecute = false, CreateNoWindow = true };
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    public static void Run(Application app, int pid)
    {
        var thread = new Thread(() =>
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.WaitForExit();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
            app.Dispatcher.BeginInvoke(() => Evaluate(app, pid));
        })
        { IsBackground = true, Name = "Watcher" };
        thread.Start();
    }

    private static void Evaluate(Application app, int pid)
    {
        var paths = AppPaths.Default;
        var flag = new DirtyFlag(paths.DirtyFlagFile).Read();
        AudioSnapshot? snap = null;
        try { snap = SnapshotStore.Load(paths.SnapshotFile); }
        catch (SnapshotException) { }

        var report = CrashCheck.Evaluate(flag, pid, () => snap is null ? null : SnapshotService.Compare(snap, ReadState()));
        if (report is null)
        {
            app.Shutdown();
            return;
        }

        var profile = ProfileStore.Load(paths.ProfileFile).Profile;
        var window = new Views.WatchWindow(report, MainViewModel.DiscordWayBack(profile.DiscordPreviousMic), () =>
        {
            if (snap is null) return null;
            return SnapshotService.Restore(snap, ReadState(), new PolicyConfigWriter());
        });
        window.Closed += (_, _) => app.Shutdown();
        window.Show();
    }

    private static AudioStateReport ReadState()
    {
        try
        {
            using var catalog = new DeviceCatalog();
            return catalog.ReadState();
        }
        catch (COMException)
        {
            return new AudioStateReport(DateTime.Now, [], []);
        }
    }
}
