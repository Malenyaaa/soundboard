using System.Diagnostics;
using System.Windows;
using Soundboard.App.ViewModels;
using Soundboard.Core.Config;
using Soundboard.Core.SystemState;

namespace Soundboard.App;

/// <summary>
/// Watcher child process (<c>Soundboard.exe --watch &lt;pid&gt;</c>). Waits until the app ends,
/// then silently puts back the Windows settings the app had changed for that run (journal) – after
/// a crash as after a normal exit, where there is simply nothing left. A small window appears only
/// if the app did not end normally while the cable was feeding Discord: the microphone in Discord
/// is silent now. No service, no admin.
/// </summary>
internal static class Watcher
{
    /// <summary>Starts the watcher for this process. Failure only means no crash handling (the next start puts things back).</summary>
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
        // First, always and without asking: what the ended run changed goes back (idempotent).
        var undone = Cli.Changes(paths).UndoSession(pid);

        var report = CrashCheck.Evaluate(flag, pid, undone);
        if (report is null)
        {
            app.Shutdown();
            return;
        }

        var profile = ProfileStore.Load(paths.ProfileFile).Profile;
        var window = new Views.WatchWindow(report, MainViewModel.DiscordWayBack(profile.DiscordPreviousMic));
        window.Closed += (_, _) => app.Shutdown();
        window.Show();
    }
}
