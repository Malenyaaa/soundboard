using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Soundboard.Core.Audio;
using Soundboard.Core.Config;
using Soundboard.Core.SystemState;
using Soundboard.Core.Text;

namespace Soundboard.App;

/// <summary>
/// Command line functions. The EXE is a GUI program, so console output needs either an
/// inherited redirect (pipe/file) or attaching to the parent console.
/// </summary>
internal static class Cli
{
    /// <summary>The console speaks the language stored in the profile (English without one). Only reads.</summary>
    public static void UseProfileLanguage() =>
        L.SetLanguage(ProfileStore.Peek(AppPaths.Default.ProfileFile)?.Language ?? L.English);

    public static int RunCheck()
    {
        using var console = ConsoleOutput.Open();
        try
        {
            using var catalog = new DeviceCatalog();
            var report = catalog.ReadState();
            console.Writer.Write(AudioStateText.Format(report));
            return 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            console.Writer.WriteLine(L.F("Check.ReadFailed", ex.Message));
            return 1;
        }
    }

    /// <summary>
    /// Puts the Windows default devices back to the saved snapshot (only those, only where
    /// they differ). Called by the uninstaller and by hand. Prints what it did.
    /// </summary>
    public static int RunRestore()
    {
        using var console = ConsoleOutput.Open();
        var w = console.Writer;
        var paths = AppPaths.Default;
        // First the settings the soundboard changed itself (journal): these go back in any case.
        int changesCode = RestoreChanges(w, paths);
        int defaultsCode = RestoreDefaults(w, paths);
        return Math.Max(changesCode, defaultsCode);
    }

    /// <summary>Puts back every Windows setting in the change journal and prints what happened.</summary>
    private static int RestoreChanges(TextWriter w, AppPaths paths)
    {
        TurnOffChangeSwitches(paths);
        var changes = Changes(paths);
        if (changes.List().Count == 0) return 0;
        var result = changes.UndoAll();
        w.WriteLine(L.Get("Cli.Changes"));
        foreach (var line in SystemChanges.Format(result).Split(Environment.NewLine)) w.WriteLine("  " + line);
        w.WriteLine();
        return result.AllDone ? 0 : 1;
    }

    /// <summary>
    /// After --restore or removal: the switches that make changes go off in the saved settings, so
    /// the next start (e.g. by autostart) does not change Windows again. Like "Undo all changes".
    /// </summary>
    public static void TurnOffChangeSwitches(AppPaths paths)
    {
        if (!File.Exists(paths.ProfileFile)) return;
        var loaded = ProfileStore.Load(paths.ProfileFile);
        if (loaded.Problem is not null) return; // unreadable settings are not overwritten
        var p = loaded.Profile;
        if (!p.KeepFullVolumeInCalls && !p.CableAt48k) return;
        p.KeepFullVolumeInCalls = false;
        p.CableAt48k = false;
        try { ProfileStore.Save(paths.ProfileFile, p); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The change journal with the real Windows behind it (used by --restore, the watcher and the removal guide).</summary>
    public static SystemChanges Changes(AppPaths paths) =>
        new(new ChangeJournal(paths.ChangesFile), new UserRegistry(), new PolicyConfigFormatApi());

    private static int RestoreDefaults(TextWriter w, AppPaths paths)
    {
        AudioSnapshot? snap;
        try
        {
            snap = SnapshotStore.Load(paths.SnapshotFile);
        }
        catch (SnapshotException ex)
        {
            w.WriteLine(ex.Message);
            return 2;
        }
        if (snap is null)
        {
            w.WriteLine(L.Get("Cli.NoSnapshot"));
            return 0;
        }

        try
        {
            using var catalog = new DeviceCatalog();
            var before = catalog.ReadState();
            var cmp = SnapshotService.Compare(snap, before);
            w.Write(SnapshotService.Format(cmp));
            if (!cmp.DefaultsChanged)
            {
                w.WriteLine(L.Get("Cli.AlreadySame"));
                return 0;
            }
            w.WriteLine();
            w.WriteLine(L.Get("Cli.Restoring"));
            var result = SnapshotService.Restore(snap, before, new PolicyConfigWriter());
            w.Write(SnapshotService.Format(result));
            return result.AllGood ? 0 : 1;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            w.WriteLine(L.F("Check.ReadFailed", ex.Message));
            return 1;
        }
    }

    public static int RunHelp()
    {
        using var console = ConsoleOutput.Open();
        console.Writer.WriteLine("Soundboard");
        console.Writer.WriteLine();
        console.Writer.WriteLine("  Soundboard.exe              " + L.Get("Cli.HelpOpen"));
        console.Writer.WriteLine("  Soundboard.exe --check      " + L.Get("Cli.HelpCheck"));
        console.Writer.WriteLine("  Soundboard.exe --restore    " + L.Get("Cli.HelpRestore"));
        console.Writer.WriteLine("  Soundboard.exe --uninstall  " + L.Get("Cli.HelpUninstall"));
        console.Writer.WriteLine("  Soundboard.exe --tray       " + L.Get("Cli.HelpTray"));
        console.Writer.WriteLine();
        console.Writer.WriteLine(L.Get("Cli.HelpWait") + "  .\\Soundboard.exe --check | Out-String");
        return 0;
    }

    /// <summary>Text output to whatever the caller provided: pipe/file, or the parent console.</summary>
    private sealed class ConsoleOutput : IDisposable
    {
        private const int StdOutput = -11;
        private const int AttachParentProcess = -1;
        private const uint FileTypeDisk = 1;
        private const uint FileTypePipe = 3;

        private ConsoleOutput(TextWriter writer) => Writer = writer;

        public TextWriter Writer { get; }

        public static ConsoleOutput Open()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var handle = GetStdHandle(StdOutput);
            uint type = handle == IntPtr.Zero || handle == new IntPtr(-1) ? 0 : GetFileType(handle);
            bool redirected = type is FileTypeDisk or FileTypePipe;

            bool attached = AttachConsole(AttachParentProcess);
            if (redirected)
            {
                // Output goes into a pipe or file; use the parent console's code page so that
                // e.g. PowerShell decodes umlauts correctly. Without a console: UTF-8.
                uint cp = attached ? GetConsoleOutputCP() : 0;
                Encoding enc;
                try { enc = cp == 0 ? new UTF8Encoding(false) : Encoding.GetEncoding((int)cp); }
                catch (ArgumentException) { enc = new UTF8Encoding(false); }
                var stream = new FileStream(new SafeFileHandle(handle, ownsHandle: false), FileAccess.Write);
                return new ConsoleOutput(new StreamWriter(stream, enc) { AutoFlush = true });
            }

            if (attached)
            {
                // The shell has already printed its prompt (it does not wait for GUI programs);
                // start on a fresh line.
                var writer = Console.Out;
                writer.WriteLine();
                return new ConsoleOutput(writer);
            }

            // Started without any console (e.g. by double click with arguments): nothing to show.
            return new ConsoleOutput(TextWriter.Null);
        }

        public void Dispose() => Writer.Flush();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int stdHandle);

        [DllImport("kernel32.dll")]
        private static extern uint GetFileType(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern uint GetConsoleOutputCP();
    }
}
