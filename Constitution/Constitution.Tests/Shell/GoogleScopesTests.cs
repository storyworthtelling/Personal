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
