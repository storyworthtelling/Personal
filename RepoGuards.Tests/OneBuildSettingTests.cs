using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RepoGuards.Tests;

/// <summary>
/// Audit W12: one place for each build setting. Every package version lives in <c>Directory.Packages.props</c> (a
/// project names a package and nothing more), the SDK and the workload set are pinned in <c>global.json</c>, every
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
                else if (!named.Contains(include) && !reference.Value.Contains("VersionOverride=\"$(MauiVersion)\"", StringComparison.Ordinal))
                    found.Add($"{relative}: {include} has no central version");
            }
        }

        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    [Fact]
    public void TheSdkAndTheWorkloadSetArePinned()
    {
        var json = File.ReadAllText(Path.Combine(Root, "global.json"));
        Assert.Matches(@"""version"":\s*""10\.0\.\d+""", json);
        Assert.Matches(@"""workloadVersion"":\s*""10\.0\.\d+""", json);
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

    [GeneratedRegex(@"<PackageReference\s+Include=""(?<name>[^""]+)""[^>]*>")]
    private static partial Regex PackageReference();
}
