using System.Text.RegularExpressions;

namespace Pulp.Tests.Infrastructure;

/// <summary>
/// D207 — the title page shows the rating chip only; the rating source is read for the code and
/// never for reasons, plot or anything else. A source-level guard over every field name the OMDb
/// client reads, so a new read is a decision rather than a diff.
/// </summary>
public class OmdbReadsTheCodeOnlyTests
{
    private static readonly string[] Allowed = ["Rated", "Response", "Error"];

    private static readonly Regex FieldRead = new(@"\.(?:GetProperty|TryGetProperty|Text|Number|Long|Flag|Child)\(\s*""(?<name>[^""]+)""");

    [Fact]
    public void TheOmdbClientReadsOnlyTheRatingCode()
    {
        var file = PulpSource.Files(".cs").Single(path => Path.GetFileName(path) == "OmdbClient.cs");

        var names = FieldRead.Matches(File.ReadAllText(file)).Select(match => match.Groups["name"].Value).ToList();

        Assert.True(names.Count > 0, "Found no field read in OmdbClient.cs; the guard would pass vacuously.");
        Assert.Contains("Rated", names);
        Assert.All(names, name => Assert.Contains(name, Allowed));
    }
}
