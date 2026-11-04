using System.IO;
using Soundboard.App.Icons;

namespace IconGen;

/// <summary>
/// Renders the app icon in its fixed colour (#9932CC) at 16, 20, 24, 32, 48 and 256 px and
/// writes it as an ICO with PNG images.
///   dotnet run --project tools/IconGen               writes src/Soundboard.App/Assets/Soundboard.ico
///   dotnet run --project tools/IconGen -- &lt;file&gt;    writes to another file
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string target;
        if (args.Length > 0) target = Path.GetFullPath(args[0]);
        else
        {
            var root = FindRepoRoot();
            if (root is null)
            {
                Console.Error.WriteLine("Soundboard.sln not found above the working directory; pass the target file.");
                return 2;
            }
            target = Path.Combine(root, "src", "Soundboard.App", "Assets", "Soundboard.ico");
        }

        var ico = IcoFile.Build(AppIconDrawing.DefaultAccent, AppIconDrawing.IcoSizes);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, ico);
        Console.WriteLine($"{target}: {ico.Length} bytes, sizes {string.Join(", ", AppIconDrawing.IcoSizes)}");
        return 0;
    }

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Soundboard.sln"))) return dir.FullName;
        return null;
    }
}
