using System.Text.RegularExpressions;

namespace Pulp.Tests.Guard;

/// <summary>
/// G7 — no TMDB in source. TMDB is excluded by decision (its terms bar this use), so its hosts must
/// not appear at all. The other half of G7, no provider key in source, is now the repo-wide
/// <c>RepoGuards.Tests/NoKeysTests</c>, which reads Pulp with everything else.
/// </summary>
public class NoTmdbTests
{
    private static readonly Regex TmdbHost = new(@"themoviedb\.org|api\.tmdb\.org", RegexOptions.IgnoreCase);

    private static readonly string[] Scanned = [".cs", ".csproj", ".xml", ".json", ".yml", ".yaml", ".props", ".targets"];

    [Fact]
    public void NoTmdbHostInPulpSource()
    {
        var offenders = SourceFiles()
            .Where(file => TmdbHost.IsMatch(File.ReadAllText(file)))
            .Select(PulpSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "TMDB is named in Pulp source:\n" + string.Join("\n", offenders));
    }

    private static IReadOnlyList<string> SourceFiles()
    {
        var files = PulpSource.Files(Scanned)
            .Where(file => !PulpSource.IsGuard(file, "NoTmdbTests.cs"))
            .ToList();
        Assert.True(files.Count > 0, "NoTmdbTests scanned no files; the guard would pass on nothing.");
        return files;
    }
}
