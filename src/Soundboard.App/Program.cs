namespace Soundboard.App;

/// <summary>Entry point: command line switches first, otherwise the window.</summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => Is(a, "--check")))
            return Cli.RunCheck();
        if (args.Any(a => Is(a, "--help") || Is(a, "-h") || Is(a, "/?")))
            return Cli.RunHelp();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static bool Is(string arg, string name) => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);
}
