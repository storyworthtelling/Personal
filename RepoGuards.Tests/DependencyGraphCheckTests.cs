using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// On 17 Nov 2026 transitive pinning set Concurrent.Futures to a version outside the exact range Concurrent.Futures.Ktx
/// asks for, and NuGet left Concurrent.Futures out of four heads' graphs without an error. The APKs built, signed and
/// shipped with no androidx.concurrent.futures classes, and each app died on open; Flyleaf's death stopped the update
/// channel, so the phone needed a factory reset. The rule that broke is general: every package a package depends on
/// must be in the graph. <c>.github/scripts/check-dependency-graph.py</c> reads every <c>project.assets.json</c> a
/// build restored and fails if one is not, and every workflow that publishes an APK runs it before the APK leaves.
/// </summary>
public class DependencyGraphCheckTests
{
    private const string Script = ".github/scripts/check-dependency-graph.py";

    [Fact]
    public void AGraphMissingAPackageThatAnotherNeedsFails()
    {
        var result = Check(Assets(
            ("Xamarin.AndroidX.Work.Runtime/2.10.2", new() { ["Xamarin.AndroidX.Concurrent.Futures.Ktx"] = "1.2.0.7" }),
            ("Xamarin.AndroidX.Concurrent.Futures.Ktx/1.2.0.7", new() { ["Xamarin.AndroidX.Concurrent.Futures"] = "[1.2.0.7, 1.2.1)" })));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Xamarin.AndroidX.Concurrent.Futures.Ktx/1.2.0.7 needs Xamarin.AndroidX.Concurrent.Futures", result.Output);
    }

    [Fact]
    public void ACompleteGraphPassesEvenWithAVersionOutsideARange()
    {
        // NU1608 (a version outside a dependency's range) is the shape every MAUI head has shipped with; the package is
        // there, so the classes are in the APK. Only an absent package is a failure.
        var result = Check(Assets(
            ("Xamarin.AndroidX.Concurrent.Futures.Ktx/1.2.0.7", new() { ["Xamarin.AndroidX.Concurrent.Futures"] = "[1.2.0.7, 1.2.1)" }),
            ("Xamarin.AndroidX.Concurrent.Futures/1.3.0.1", new())));

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public void ADependencyIsMatchedWithoutRegardToCase()
    {
        var result = Check(Assets(
            ("MailKit/4.18.2", new() { ["mimekit"] = "4.18.2" }),
            ("MimeKit/4.18.2", new())));

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public void NoAssetsFileAtAllFails()
    {
        // A check that found nothing to read would pass every build; a step that runs before the restore, or from the
        // wrong folder, must say so.
        var folder = Directory.CreateTempSubdirectory("graph-check-");
        var result = Run(folder.FullName);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void EveryWorkflowThatPublishesAnApkChecksTheGraphBeforeItLeaves()
    {
        var wrong = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(GuardScan.RepoRoot(), ".github", "workflows"), "*.yml").Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(path);
            if (!text.Contains("dotnet publish", StringComparison.Ordinal))
                continue;

            var check = text.IndexOf("python3 " + Script, StringComparison.Ordinal);
            var publish = Regex.Match(text, @"dotnet publish").Index;
            var leaves = new[] { "actions/upload-artifact@", "publish-rolling-release" }
                .Select(marker => text.IndexOf(marker, StringComparison.Ordinal))
                .Where(index => index >= 0)
                .DefaultIfEmpty(int.MaxValue)
                .Min();

            if (check < 0)
                wrong.Add($"{Path.GetFileName(path)} publishes an APK without running {Script}");
            else if (check < publish || check > leaves)
                wrong.Add($"{Path.GetFileName(path)} runs {Script} before its build or after the APK has left");
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    private static string Assets(params (string Key, Dictionary<string, string> Dependencies)[] packages)
    {
        var target = packages.ToDictionary(
            package => package.Key,
            package => (object)new Dictionary<string, object> { ["type"] = "package", ["dependencies"] = package.Dependencies });
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["version"] = 3,
            ["targets"] = new Dictionary<string, object> { ["net10.0-android36.0"] = target },
        });
    }

    private static (int ExitCode, string Output) Check(string assets)
    {
        var folder = Directory.CreateTempSubdirectory("graph-check-");
        var obj = Directory.CreateDirectory(Path.Combine(folder.FullName, "Head", "obj"));
        File.WriteAllText(Path.Combine(obj.FullName, "project.assets.json"), assets);
        return Run(folder.FullName);
    }

    private static (int ExitCode, string Output) Run(string folder)
    {
        var start = new ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = folder,
        };
        start.ArgumentList.Add(Path.Combine(GuardScan.RepoRoot(), Script));
        start.ArgumentList.Add(folder);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
