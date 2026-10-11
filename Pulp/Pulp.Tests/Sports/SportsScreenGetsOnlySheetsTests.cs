using System.Text.RegularExpressions;

namespace Pulp.Tests.Sports;

/// <summary>
/// G15's other end: the head hands the Sports screen the held or open sheet and nothing rawer. The
/// composition root's public surface is read, and no public member takes or returns a raw
/// <c>LeagueFeed</c> (the Calendar, which builds fixtures from feeds, gets an internal door, and a
/// fixture carries no standing or highlight).
/// </summary>
public class SportsScreenGetsOnlySheetsTests
{
    private static string Services() =>
        File.ReadAllText(Path.Combine(PulpSource.Root(), "Pulp.Android", "App", "PulpServices.cs"));

    [Fact]
    public void TheScreensReadsReturnAPageOfSheets()
    {
        var source = Services();

        Assert.Matches(@"public\s+(?:async\s+)?Task<SportsPage>\s+SportsAsync\(", source);
        Assert.Matches(@"public\s+(?:async\s+)?Task<SportsPage>\s+CachedSportsAsync\(", source);
    }

    [Fact]
    public void NoPublicMemberOfTheCompositionRootTakesOrReturnsARawFeed()
    {
        var publicLines = Services().Split('\n')
            .Where(line => Regex.IsMatch(line, @"^\s*public\s") && !line.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .ToList();

        Assert.True(publicLines.Count >= 30, $"Only {publicLines.Count} public members found; the guard is not reading the composition root.");
        Assert.DoesNotContain(publicLines, line => line.Contains("LeagueFeed", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCalendarsDoorToTheFeedsIsInternalAndSaysWhyItExists()
    {
        Assert.Matches(@"internal\s+(?:async\s+)?Task<IReadOnlyList<LeagueFeed>>\s+FollowedFeedsAsync\(", Services());
    }
}
