using System.Text.RegularExpressions;
using Pulp.Domain.ChildView;
using Pulp.Domain.Shell;

namespace Pulp.Tests.ChildView;

/// <summary>
/// The child's view, held structurally (D12, D231, D23, G13). In a child's view no area may show a
/// title the child's filter hides, and no Settings, Vault or Household entry may be reachable. The head
/// is not compiled in this suite, so the Android half is read by content: the shell builds its binder
/// from the child's three areas and routes every requested area through the session; each area hands
/// its page to a child's page in the child's view; and the child's pages read no title except through
/// the child's audience, hand off only for the children, record the sitting as the child's, and name no
/// adult screen. Every check asserts it found what it protects, so none passes on an empty tree.
/// </summary>
public class ChildViewReachTests
{
    private const string ShellPath = "Pulp.Android/MainActivity.cs";

    // The adult screens a child's page must never open: settings, the vault, the household and its
    // services, first run, Recently watched, and the adult title page (whose Play is the phone owner's).
    private static readonly Regex AdultScreen = new(
        @"\b(SettingsArea|SettingsActivity|VaultActivity|HouseholdActivity|ServiceActivity|FirstRunActivity|RecentlyWatchedActivity|TitleActivity)\b");

    // A read that brings titles onto a page.
    private static readonly Regex TitleRead = new(
        @"\.(TitleAsync|SearchAsync|ContinueWatchingAsync|PlaylistsAsync|PlaylistsFor|AskMoodAsync|StarsOf|CachedCopy)\s*\(");

    // The audience's own ways of deciding what a child may see.
    private static readonly Regex AudienceFilter = new(
        @"\baudience\.(Shows|Visible)\s*\(|\bChildFavorites\.For\s*\(|\bChildPlaylists\.For\s*\(|\baudience\.Mood\b");

    private static IReadOnlyList<(string Path, string Text)> Head() =>
        PulpSource.Files(".cs")
            .Select(file => (Path: PulpSource.Relative(file), Text: File.ReadAllText(file)))
            .Where(file => file.Path.StartsWith("Pulp.Android/", StringComparison.Ordinal))
            .ToList();

    /// <summary>The child's pages: everything under Favorites/, and the shell's Child* pages.</summary>
    private static IReadOnlyList<(string Path, string Text)> ChildPages()
    {
        var pages = Head()
            .Where(file => file.Path.StartsWith("Pulp.Android/Favorites/", StringComparison.Ordinal)
                || Regex.IsMatch(file.Path, @"^Pulp\.Android/Shell/Child\w*\.cs$"))
            .ToList();
        Assert.True(pages.Count >= 4, "The child's pages were not found; the guard has nothing to protect: " + string.Join(", ", pages.Select(page => page.Path)));
        return pages;
    }

    [Fact]
    public void NoChildAreaIsSettingsSportsOrCalendar()
    {
        var areas = ChildViewAreas.For(new ChildViewSession([ChildViewFixtures.Robin], PulpArea.Watching));

        Assert.NotEmpty(areas);
        Assert.DoesNotContain(PulpArea.Settings, areas);
        Assert.DoesNotContain(PulpArea.Sports, areas);
        Assert.DoesNotContain(PulpArea.Calendar, areas);
    }

    [Fact]
    public void EveryAreaRequestedInAChildsViewLandsOnAChildsArea()
    {
        var session = new ChildViewSession([ChildViewFixtures.Robin], PulpArea.Settings);

        foreach (var spec in PulpAreas.All)
        {
            Assert.Contains(session.Route(spec.Area), session.Areas);
        }
    }

    [Fact]
    public void TheShellBuildsItsBinderFromTheChildsAreasAndRoutesEveryRequest()
    {
        var shell = Head().Single(file => file.Path == ShellPath).Text;

        Assert.Matches(@"ChildViewAreas\.For\s*\(", shell);
        Assert.Matches(@"\.Route\s*\(", shell);

        // The one place a requested area is read must route it.
        var requested = Regex.Match(shell, @"private\s+int\?\s+RequestedIndex\s*\((?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(requested.Success, "MainActivity has no RequestedIndex to check.");
        Assert.Contains("Route(", requested.Groups["body"].Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Pulp.Android/Shell/WatchingArea.cs", "ChildWatchingPage")]
    [InlineData("Pulp.Android/Shell/PlaylistsArea.cs", "ChildPlaylistsPage")]
    [InlineData("Pulp.Android/Shell/DiscoverArea.cs", "ChildDiscoverPage")]
    public void EachChildAreaHandsItsPageToAChildsPage(string path, string page)
    {
        var text = Head().Single(file => file.Path == path).Text;

        Assert.Matches(@"override\s+void\s+ViewAs\s*\(", text);
        Assert.Contains(page, text, StringComparison.Ordinal);
    }

    [Fact]
    public void AChildsPageOpensNoAdultScreen()
    {
        var offenders = ChildPages()
            .SelectMany(page => AdultScreen.Matches(page.Text).Select(match => $"{page.Path}: {match.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0, "A child's page names an adult screen:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void AChildsPageReadsTitlesOnlyThroughTheChildsAudience()
    {
        var reading = ChildPages().Where(page => TitleRead.IsMatch(page.Text)).ToList();
        Assert.True(reading.Count >= 3, "No child's page reads titles; the guard has nothing to protect.");

        var unfiltered = reading.Where(page => !AudienceFilter.IsMatch(page.Text)).Select(page => page.Path).ToList();
        Assert.True(unfiltered.Count == 0, "A child's page reads titles without the child's filter: " + string.Join(", ", unfiltered));
    }

    [Fact]
    public void AChildsPageNeverAsksWhosePhoneItIs()
    {
        var offenders = ChildPages().Where(page => Regex.IsMatch(page.Text, @"\bThisPhone(Of|Async)?\b")).Select(page => page.Path).ToList();

        Assert.True(offenders.Count == 0, "A child's page reads as the phone's adult: " + string.Join(", ", offenders));
    }

    [Fact]
    public void AChildsHandOffIsForTheChildrenAndRecordedAsTheirs()
    {
        var pages = ChildPages();
        var requests = pages.SelectMany(page => Regex.Matches(page.Text, @"new\s+HandOffRequest\s*\(\s*(?<who>[^,]+),").Select(match => (page.Path, Who: match.Groups["who"].Value.Trim()))).ToList();
        var sittings = pages.SelectMany(page => Regex.Matches(page.Text, @"new\s+WatchedSitting\s*\(\s*(?<who>[^,]+),").Select(match => (page.Path, Who: match.Groups["who"].Value.Trim()))).ToList();

        Assert.True(requests.Count > 0, "No child's page hands a title off; the guard has nothing to protect.");
        Assert.True(sittings.Count > 0, "No child's page records a hand-off; the guard has nothing to protect.");
        Assert.All(requests, request => Assert.Equal("audience.Watching", request.Who));
        Assert.All(sittings, sitting => Assert.Equal("child", sitting.Who));
        Assert.All(pages.Where(page => page.Text.Contains("new WatchedSitting(", StringComparison.Ordinal)),
            page => Assert.Matches(@"foreach\s*\(\s*var\s+child\s+in\s+audience\.Watching\s*\)", page.Text));
    }
}
