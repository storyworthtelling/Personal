using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RepoGuards.Tests;

/// <summary>
/// Audit W12: one place for each build setting. Every package version lives in <c>Directory.Packages.props</c> (a
/// project names a package and nothing more), the SDK is pinned in <c>global.json</c> (and only the SDK), every
/// Android project has the one minimum (API 34, the suite's phones), and every test project collects coverage.
/// </summary>
public partial class OneBuildSettingTests
{
    private static string Root => GuardScan.RepoRoot();

    private static IEnumerable<(string Relative, string Text)> Projects() =>
        GuardScan.Files(Root)
            .Where(file => file.EndsWith(".csproj", StringComparison.Ordinal))
            .Select(file => (Path.GetRelativePath(Root, file).Replace('\\', '/'), File.ReadAllText(file)));

    [Fact]
    public void EveryPackageVersionIsCentral()
    {
        var props = XDocument.Load(Path.Combine(Root, "Directory.Packages.props"));
        Assert.Equal("true", props.Descendants("ManagePackageVersionsCentrally").Single().Value);
        Assert.Equal("true", props.Descendants("CentralPackageTransitivePinningEnabled").Single().Value);
        Assert.DoesNotContain(props.Descendants("PackageVersion"), item => ((string?)item.Attribute("Version") ?? "").Contains('*'));

        var named = props.Descendants("PackageVersion").Select(item => (string)item.Attribute("Include")!).ToHashSet();
        var found = new List<string>();
        foreach (var (relative, text) in Projects())
        {
            foreach (Match reference in PackageReference().Matches(text))
            {
                var include = reference.Groups["name"].Value;
                if (reference.Value.Contains(" Version=", StringComparison.Ordinal))
                    found.Add($"{relative}: {include} names its own version");
                else if (reference.Value.Contains("VersionOverride=", StringComparison.Ordinal) && !AllowedOverrides.Contains(include))
                    found.Add($"{relative}: {include} names its own version");
                else if (!named.Contains(include) && !reference.Value.Contains("VersionOverride=\"$(MauiVersion)\"", StringComparison.Ordinal))
                    found.Add($"{relative}: {include} has no central version");
            }
        }

        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    /// <summary>
    /// WorkManager 2.10 needs androidx.concurrent.futures, which reaches a head only through Concurrent.Futures.Ktx's
    /// exact range [1.2.0.7, 1.2.1). With transitive pinning, the central 1.3.0.1 (which AndroidX.Core and CameraX
    /// want) sits outside that range, and NuGet then leaves the package out of the graph without an error: the APK
    /// builds and signs with no androidx.concurrent.futures classes, and the app dies as its process starts, the first
    /// time WorkManager is asked for anything. That shipped Flyleaf, Post, Pulp and Constitution on 17 Nov 2026, and
    /// Flyleaf's death took the update channel with it. Naming the package directly keeps it in every APK.
    /// </summary>
    [Fact]
    public void EveryProjectWithWorkManagerNamesConcurrentFuturesItself()
    {
        var found = Projects()
            .Where(project => PackageReference().Matches(project.Text).Any(reference => reference.Groups["name"].Value == "Xamarin.AndroidX.Work.Runtime"))
            .Where(project => PackageReference().Matches(project.Text).All(reference => reference.Groups["name"].Value != "Xamarin.AndroidX.Concurrent.Futures"))
            .Select(project => $"{project.Relative} references Work.Runtime without Concurrent.Futures")
            .ToList();

        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    [Fact]
    public void TheSdkIsPinnedAndTheWorkloadIsNot()
    {
        var json = File.ReadAllText(Path.Combine(Root, "global.json"));
        Assert.Matches(@"""version"":\s*""10\.0\.\d+""", json);
        // A workloadVersion in global.json makes every dotnet command resolve that workload set, so every job
        // that installs no workload (the guards, the libraries' tests) fails before it builds anything.
        Assert.DoesNotContain("workloadVersion", json, StringComparison.Ordinal);
        Assert.Contains("global-json-file: global.json", File.ReadAllText(Path.Combine(Root, ".github/actions/dotnet/action.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryAndroidProjectHasTheOneMinimum()
    {
        var found = Projects()
            .Where(project => project.Text.Contains("net10.0-android", StringComparison.Ordinal)
                && Regex.IsMatch(project.Text, @"<TargetFrameworks?>[^<]*net10\.0-android|ForewordTfms|LedgerTfms|FolioTfms"))
            .Where(project => !project.Text.Contains(">34</SupportedOSPlatformVersion>", StringComparison.Ordinal)
                || project.Text.Contains("AndroidMinSdkVersion", StringComparison.Ordinal))
            .Select(project => project.Relative)
            .ToList();

        Assert.True(found.Count == 0, "Set SupportedOSPlatformVersion 34 and nothing else:\n" + string.Join("\n", found));
    }

    [Fact]
    public void EveryTestProjectCollectsCoverage()
    {
        var found = Projects()
            .Where(project => project.Text.Contains("Include=\"xunit\"", StringComparison.Ordinal)
                && !project.Text.Contains("coverlet.collector", StringComparison.Ordinal))
            .Select(project => project.Relative)
            .ToList();

        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    // The overrides that are decisions: MAUI's version comes from its workload, Plate's AppCompat sits above MAUI's
    // (a pin of 1.8.0 for everyone breaks the MAUI heads' Lifecycle graph), and Flyleaf's LiveData sits above every
    // other head's (P14 W-3.1.3: Play services' Fragment 1.9 needs LiveData.Core 2.11, which WorkManager 2.10's LiveData
    // 2.9.1 refuses by an exact range; raising it for everyone would move every head's Lifecycle graph).
    private static readonly HashSet<string> AllowedOverrides = ["Microsoft.Maui.Controls", "Xamarin.AndroidX.AppCompat", "Xamarin.AndroidX.Lifecycle.LiveData"];

    [GeneratedRegex(@"<PackageReference\s+Include=""(?<name>[^""]+)""[^>]*>")]
    private static partial Regex PackageReference();
}
