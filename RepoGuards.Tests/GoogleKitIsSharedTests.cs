using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// The Google pieces every head needs live once (audit 01 §8–10): twelve copies of the Android HTTP transport for the
/// Google SDK, five device-account token providers, three AppSigningInfo copies and nineteen Drive service
/// constructions became <c>Storyworthtelling.Google.Android</c> (and the kit's sign-in flow for the MAUI heads).
/// </summary>
public partial class GoogleKitIsSharedTests
{
    [Theory]
    [InlineData(@"class \w*HttpClientFactory\s*:\s*HttpClientFactory", "Storyworthtelling.Google.Android/GoogleHttpClientFactory.cs")]
    [InlineData(@"\bclass AppSigningInfo\b", "Storyworthtelling.Google.Android/AppSigningInfo.cs")]
    // The log reader runs on a desktop and in CI, not on a phone, so it builds its own on the default HTTP stack.
    [InlineData(@"new DriveService\(new ", "Storyworthtelling.Google.Android/GoogleDrive.cs", "Tools/LogLoopReader/DriveLogSource.cs")]
    [InlineData(@"\bBlockingGetAuthToken\(", "Storyworthtelling.Google.Android/DeviceAccountTokens.cs", "Storyworthtelling.Maui/Google/DeviceAccountSignInFlow.cs")]
    public void OnlyTheKitDoesThis(string pattern, params string[] allowed)
    {
        var found = Sources()
            .Where(file => Regex.IsMatch(File.ReadAllText(file), pattern))
            .Select(Relative)
            .Where(file => !allowed.Contains(file))
            .ToList();

        Assert.True(found.Count == 0, $"Use the Google kit instead ({pattern}):\n" + string.Join("\n", found));
    }

    [Fact]
    public void EveryDriveQueryIsBuiltByDriveQuery()
    {
        // A hand-written q with an interpolated value is how an apostrophe in a title broke a query (audit 01 §17).
        var found = Sources()
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(hit => Regex.IsMatch(hit.line, @"\.Q\s*=\s*\$?"""))
            .Select(hit => $"{Relative(hit.file)}:{hit.index + 1}")
            .ToList();

        Assert.True(found.Count == 0, "Build a Drive query with Storyworthtelling.Voice.DriveQuery:\n" + string.Join("\n", found));
    }

    [Fact]
    public void DriveListingsPageThroughOneHelper()
    {
        // A PageToken loop written by hand is DrivePaging again; a list without one reads a single page.
        var found = Sources()
            .Where(file => Relative(file) != "Storyworthtelling.Voice/DriveQuery.cs")
            .Select(file => (file, text: File.ReadAllText(file)))
            .Where(hit => hit.text.Contains("Files.List(", StringComparison.Ordinal)
                && hit.text.Contains(".PageToken = ", StringComparison.Ordinal))
            .Select(hit => hit.file)
            .Select(Relative)
            .ToList();

        Assert.True(found.Count == 0, "Use DrivePaging.ListFilesAsync rather than a hand-written page loop:\n" + string.Join("\n", found));
    }

    [Fact]
    public void TheDeadBrokerAndWeatherClientAreGone()
    {
        Assert.False(File.Exists(Path.Combine(GuardScan.RepoRoot(), "Storyworthtelling.Google", "IGoogleAccountBroker.cs")));
        Assert.False(File.Exists(Path.Combine(GuardScan.RepoRoot(), "Storyworthtelling.Google", "GoogleWeatherClient.cs")));
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "Watch"))
            .Where(path => !path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');
}
