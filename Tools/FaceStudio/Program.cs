using System.Globalization;

namespace FaceStudio;

/// <summary>
/// <c>dotnet run --project Tools/FaceStudio -- --repo-root . [--apk path] [--version-code n] [--version-name s]</c>:
/// writes the face into <see cref="FaceFiles.Folder"/>, and with <c>--apk</c> packs it into an unsigned APK.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var options = Options(args);
        var root = Path.GetFullPath(options.GetValueOrDefault("--repo-root", "."));
        if (!File.Exists(Path.Combine(root, "Directory.Packages.props")))
        {
            Console.Error.WriteLine($"{root} is not the repository's root (no Directory.Packages.props).");
            return 2;
        }

        var folder = Path.Combine(root, FaceFiles.Folder);
        FaceFiles.Write(root, folder);
        Console.WriteLine($"Wrote the face to {FaceFiles.Folder}.");

        if (!options.TryGetValue("--apk", out var apk))
        {
            return 0;
        }

        var aapt2 = Apk.FindAapt2();
        var androidJar = Apk.FindAndroidJar();
        if (aapt2 is null || androidJar is null)
        {
            Console.Error.WriteLine($"No aapt2 or no android-{PointFace.MinSdk} android.jar; run FocusPhone/scripts/setup-android-build.sh.");
            return 3;
        }

        var code = int.Parse(options.GetValueOrDefault("--version-code", "1"), CultureInfo.InvariantCulture);
        Apk.Build(folder, Path.GetFullPath(apk), aapt2, androidJar, code, options.GetValueOrDefault("--version-name", "1.0"));
        Console.WriteLine($"Packed {apk} (unsigned).");
        return 0;
    }

    private static Dictionary<string, string> Options(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException($"Expected '--name value' pairs, found '{args[index]}'.");
            }
            options[args[index]] = args[++index];
        }
        return options;
    }
}
