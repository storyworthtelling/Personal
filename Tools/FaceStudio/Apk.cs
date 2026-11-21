using System.Diagnostics;
using System.Globalization;

namespace FaceStudio;

/// <summary>
/// Packs the face's folder into an unsigned APK with aapt2: compile the resources, then link them with
/// the manifest against the platform's <c>android.jar</c>. Signing is CI's (the suite's one keystore).
/// </summary>
public static class Apk
{
    /// <summary>aapt2 from the Android SDK (ANDROID_HOME, the minimal SDK), else the .NET Android workload's own copy.</summary>
    public static string? FindAapt2()
    {
        var fromSdk = SdkRoots()
            .Select(root => Path.Combine(root, "build-tools"))
            .Where(Directory.Exists)
            .SelectMany(tools => NewestFirst(Directory.EnumerateDirectories(tools)))
            .Select(tools => Path.Combine(tools, "aapt2"))
            .FirstOrDefault(File.Exists);
        if (fromSdk is not null)
        {
            return fromSdk;
        }

        var packs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "packs", "Microsoft.Android.Sdk.Linux");
        return Directory.Exists(packs)
            ? NewestFirst(Directory.EnumerateDirectories(packs))
                .Select(pack => Path.Combine(pack, "tools", "Linux", "aapt2"))
                .FirstOrDefault(File.Exists)
            : null;
    }

    /// <summary>
    /// Version folders, newest first by number (as text, "9.0.0" sorts after "36.0.0"); a pre-release
    /// ("36.0.0-rc1") goes after its release, and a folder that names no version goes last.
    /// </summary>
    public static IReadOnlyList<string> NewestFirst(IEnumerable<string> folders) =>
        [.. folders
            .Select(folder => (Folder: folder, Name: Path.GetFileName(folder.TrimEnd('/', '\\'))))
            .Select(entry => (entry.Folder, Version: VersionOf(entry.Name), PreRelease: entry.Name.Contains('-', StringComparison.Ordinal)))
            .OrderByDescending(entry => entry.Version is not null)
            .ThenByDescending(entry => entry.Version)
            .ThenBy(entry => entry.PreRelease)
            .ThenBy(entry => entry.Folder, StringComparer.Ordinal)
            .Select(entry => entry.Folder)];

    // The numbers before any pre-release tag ("36.0.0-rc1" is 36.0.0), or none.
    private static Version? VersionOf(string name)
    {
        var numbers = name.Split('-', 2)[0];
        return Version.TryParse(numbers.Contains('.', StringComparison.Ordinal) ? numbers : numbers + ".0", out var version) ? version : null;
    }

    /// <summary>The platform the face is built against: API <see cref="PointFace.MinSdk"/>.</summary>
    public static string? FindAndroidJar() =>
        SdkRoots()
            .Select(root => Path.Combine(root, "platforms", "android-" + PointFace.MinSdk, "android.jar"))
            .FirstOrDefault(File.Exists);

    public static void Build(string faceFolder, string outputApk, string aapt2, string androidJar, int versionCode, string versionName)
    {
        var work = Directory.CreateTempSubdirectory("facestudio-compile-");
        try
        {
            var compiled = Path.Combine(work.FullName, "resources.zip");
            Run(aapt2, ["compile", "--dir", Path.Combine(faceFolder, "res"), "-o", compiled]);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputApk))!);
            Run(aapt2,
            [
                "link", "-o", outputApk, "-I", androidJar,
                "--manifest", Path.Combine(faceFolder, "AndroidManifest.xml"),
                "--version-code", versionCode.ToString(CultureInfo.InvariantCulture),
                "--version-name", versionName,
                compiled,
            ]);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    /// <summary>The built APK's manifest as aapt2 reads it back.</summary>
    public static string DumpManifest(string apk, string aapt2) =>
        Run(aapt2, ["dump", "xmltree", "--file", "AndroidManifest.xml", apk]);

    private static IEnumerable<string> SdkRoots() =>
        new[] { Environment.GetEnvironmentVariable("ANDROID_HOME"), Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"), "/opt/android-sdk-min" }
            .OfType<string>()
            .Where(root => root.Length > 0 && Directory.Exists(root));

    private static string Run(string tool, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{tool} did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(tool)} {arguments[0]} failed ({process.ExitCode}): {error.Trim()}");
        }
        return output.Result;
    }
}
