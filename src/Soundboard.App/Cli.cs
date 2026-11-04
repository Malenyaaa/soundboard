using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Soundboard.Core.Audio;
using Soundboard.Core.Config;
using Soundboard.Core.SystemState;

namespace Soundboard.App;

/// <summary>
/// Command line functions. The EXE is a GUI program, so console output needs either an
/// inherited redirect (pipe/file) or attaching to the parent console.
/// </summary>
internal static class Cli
{
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
            console.Writer.WriteLine($"Der Audio-Zustand ließ sich nicht lesen: {ex.Message}");
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
            w.WriteLine("Es gibt keine Sicherung des Audio-Zustands. Es wurde nichts verändert.");
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
                w.WriteLine("Die Standardgeräte sind wie gesichert. Es wurde nichts verändert.");
                return 0;
            }
            w.WriteLine();
            w.WriteLine("Standardgeräte zurückstellen:");
            var result = SnapshotService.Restore(snap, before, new PolicyConfigWriter());
            w.Write(SnapshotService.Format(result));
            return result.AllGood ? 0 : 1;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            w.WriteLine($"Der Audio-Zustand ließ sich nicht lesen: {ex.Message}");
            return 1;
        }
    }

    public static int RunHelp()
    {
        using var console = ConsoleOutput.Open();
        console.Writer.WriteLine("Soundboard");
        console.Writer.WriteLine();
        console.Writer.WriteLine("  Soundboard.exe            Fenster öffnen");
        console.Writer.WriteLine("  Soundboard.exe --check    Audio-Zustand als Text ausgeben (nur lesen, ändert nichts)");
        console.Writer.WriteLine("  Soundboard.exe --restore  Windows-Standardgeräte auf die gesicherten zurückstellen");
        console.Writer.WriteLine();
        console.Writer.WriteLine("In PowerShell auf die Ausgabe warten:  .\\Soundboard.exe --check | Out-String");
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
