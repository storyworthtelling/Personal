using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// APKs for the form tests: small zips holding a compiled-manifest stand-in, and, where the Android SDK's aapt2 is on
/// this machine, real APKs linked from a manifest. Every package and version here is invented.
/// </summary>
internal static class ProbeApks
{
    public const string WatchManifest = """
        <?xml version="1.0" encoding="utf-8"?>
        <manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.example.probe"
            android:versionCode="1100000123" android:versionName="1.0">
          <uses-sdk android:minSdkVersion="34" android:targetSdkVersion="36" />
          <uses-feature android:name="android.hardware.type.watch" />
          <application android:hasCode="false" android:label="Probe" />
        </manifest>
        """;

    // The same app, declaring the watch feature as optional: still a watch app, and still not for the phone.
    public static readonly string OptionalWatchManifest = WatchManifest.Replace(
        "<uses-feature android:name=\"android.hardware.type.watch\" />",
        "<uses-feature android:name=\"android.hardware.type.watch\" android:required=\"false\" />",
        StringComparison.Ordinal);

    public static readonly string PhoneManifest = WatchManifest.Replace(
        "  <uses-feature android:name=\"android.hardware.type.watch\" />\n", "", StringComparison.Ordinal);

    /// <summary>A zip whose AndroidManifest.xml holds these strings in a compiled manifest's string-pool encoding.</summary>
    public static MemoryStream Zip(Encoding encoding, params string[] pool)
    {
        var bytes = new List<byte> { 0x03, 0x00, 0x08, 0x00 };
        foreach (var text in pool)
        {
            bytes.AddRange([0x00, 0x00]);
            bytes.AddRange(encoding.GetBytes(text));
        }
        return ZipOf(("AndroidManifest.xml", bytes.ToArray()), ("resources.arsc", [0x02, 0x00]));
    }

    public static MemoryStream ZipOf(params (string Name, byte[] Bytes)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    /// <summary>aapt2 and an android.jar to link against, or null when this machine has no Android SDK.</summary>
    public static (string Aapt2, string AndroidJar)? Sdk()
    {
        var roots = new[]
            {
                Environment.GetEnvironmentVariable("ANDROID_HOME"),
                Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
                "/opt/android-sdk-min",
            }
            .Where(root => !string.IsNullOrEmpty(root) && Directory.Exists(root));
        foreach (var root in roots)
        {
            var aapt2 = Directory.Exists(Path.Combine(root!, "build-tools"))
                ? Directory.EnumerateDirectories(Path.Combine(root!, "build-tools"))
                    .OrderByDescending(dir => Version.TryParse(Path.GetFileName(dir), out var version) ? version : new Version())
                    .Select(dir => Path.Combine(dir, "aapt2"))
                    .FirstOrDefault(File.Exists)
                : null;
            var jar = Directory.Exists(Path.Combine(root!, "platforms"))
                ? Directory.EnumerateDirectories(Path.Combine(root!, "platforms"), "android-*")
                    .OrderByDescending(dir => int.TryParse(Path.GetFileName(dir)["android-".Length..], out var level) ? level : 0)
                    .Select(dir => Path.Combine(dir, "android.jar"))
                    .FirstOrDefault(File.Exists)
                : null;
            if (aapt2 is not null && jar is not null)
            {
                return (aapt2, jar);
            }
        }
        return null;
    }

    /// <summary>Links a real, code-free APK from a manifest with aapt2.</summary>
    public static string Link((string Aapt2, string AndroidJar) sdk, string manifest, string folder, string name)
    {
        var manifestPath = Path.Combine(folder, name + ".xml");
        File.WriteAllText(manifestPath, manifest);
        var apk = Path.Combine(folder, name);
        var run = Run(sdk.Aapt2, "link", "-I", sdk.AndroidJar, "--manifest", manifestPath, "-o", apk);
        Assert.True(run.ExitCode == 0, "aapt2 link failed: " + run.Output);
        return apk;
    }

    public static (int ExitCode, string Output) Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.Result + error.Result);
    }
}
