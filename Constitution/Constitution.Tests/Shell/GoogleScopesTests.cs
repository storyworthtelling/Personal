using System.Text.RegularExpressions;

namespace Constitution.Tests.Shell;

/// <summary>
/// Every place that signs in must ask for the same scopes, or the token one screen obtained would
/// not cover the call another makes. So the head builds its Google account in exactly one place,
/// and that place asks for Calendar (the plan's full calendar scope) on top of the shared Drive one.
/// </summary>
public sealed class GoogleScopesTests
{
    private static readonly Regex Construction = new(@"new\s+VoiceGoogleAccount\s*\(", RegexOptions.Compiled);

    [Fact]
    public void OnlyTheFactoryBuildsAGoogleAccount()
    {
        var files = ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).Contains("Constitution.Android", StringComparison.Ordinal))
            .ToList();
        Assert.True(files.Count >= 10, "The scan should cover the head's sources, found " + files.Count);

        var offenders = files
            .Where(file => !Path.GetFileName(file).Equals("ConstitutionGoogle.cs", StringComparison.Ordinal))
            .Where(file => Construction.IsMatch(File.ReadAllText(file)))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Build the account through ConstitutionGoogle.Create so every screen asks for the same scopes:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void TheFactoryAsksForCalendar()
    {
        var factory = ConstitutionSource.Files(".cs")
            .SingleOrDefault(file => Path.GetFileName(file).Equals("ConstitutionGoogle.cs", StringComparison.Ordinal));
        Assert.NotNull(factory);

        var text = File.ReadAllText(factory!);
        Assert.Contains("https://www.googleapis.com/auth/calendar", text, StringComparison.Ordinal);
        Assert.Matches(Construction, text);
    }
}

/// <summary>
/// The signed Release build has no INTERNET permission unless the head declares it: Debug adds it
/// automatically, which hides the omission until the first main build. Vault restore, reMarkable
/// pairing and the voice calls would then fail without any message.
/// </summary>
public sealed class ManifestPermissionTests
{
    [Fact]
    public void TheHeadDeclaresInternet()
    {
        var sources = ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).Contains("Constitution.Android", StringComparison.Ordinal))
            .ToList();
        Assert.True(sources.Count >= 10, "The scan should cover the head's sources, found " + sources.Count);

        var declared = sources.Any(file => File.ReadAllText(file)
            .Contains("[assembly: UsesPermission(Android.Manifest.Permission.Internet)]", StringComparison.Ordinal));
        Assert.True(declared, "Declare INTERNET on the head: [assembly: UsesPermission(Android.Manifest.Permission.Internet)].");
    }

    [Fact]
    public void TheWorkflowChecksTheBuiltApkForInternet()
    {
        var workflow = Path.Combine(ConstitutionSource.Root(), "..", ".github", "workflows", "build-constitution.yml");
        Assert.True(File.Exists(workflow), "build-constitution.yml was not found at " + workflow);
        var text = File.ReadAllText(workflow);
        Assert.Contains("INTERNET", text, StringComparison.Ordinal);
        Assert.Contains("dump permissions", text, StringComparison.Ordinal);
    }
}
