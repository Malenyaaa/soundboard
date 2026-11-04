using System.Globalization;

namespace Soundboard.App;

/// <summary>Entry point: command line switches first, otherwise the window.</summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => Is(a, "--check")))
            return Cli.RunCheck();
        if (args.Any(a => Is(a, "--restore")))
            return Cli.RunRestore();
        if (args.Any(a => Is(a, "--help") || Is(a, "-h") || Is(a, "/?")))
            return Cli.RunHelp();

        if (args.Any(a => Is(a, "--uninstall")))
        {
            App.Mode = StartMode.Uninstall;
            return RunApp();
        }

        int watch = Array.FindIndex(args, a => Is(a, "--watch"));
        if (watch >= 0)
        {
            if (watch + 1 >= args.Length || !int.TryParse(args[watch + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
                return 2;
            App.Mode = StartMode.Watch;
            App.WatchPid = pid;
            return RunApp();
        }

        var instance = SingleInstance.TryAcquire();
        if (instance is null) return 0; // the running copy shows its window
        App.Instance = instance;
        App.StartInTray = args.Any(a => Is(a, "--tray"));
        return RunApp();
    }

    private static int RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static bool Is(string arg, string name) => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);
}
