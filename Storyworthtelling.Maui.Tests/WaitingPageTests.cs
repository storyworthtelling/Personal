using System.Text.RegularExpressions;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// The dog-ear's notice fold turned into a page (DESIGN.md §13, D162): <c>WaitingPage</c> is what
/// <c>DogEar.NoticesOpened</c> had nothing to open into before.
///
/// <para><c>WaitingPage : ContentPage</c> is a real MAUI view and cannot be meaningfully
/// constructed in a plain xunit run — MAUI views need a running app host (<c>Application.Current</c>
/// for <c>PaperResources</c> alone), which this test project does not carry, and
/// <c>Storyworthtelling.Maui/Views/*.cs</c> is deliberately left out of this project's direct
/// <c>Compile Include</c> list for exactly that reason (see the .csproj's own remarks; only
/// <c>DesignTokens.cs</c>/<c>Layout</c>/<c>State</c> are compiled straight in — see
/// <c>BinderTabsTests</c>/<c>DogEarPaletteTests</c> for the same read-from-source approach used
/// elsewhere in this project). So this is a source-level guard: it reads <c>WaitingPage.cs</c>'s
/// own text and asserts the shape the page must have — constructable from a notice list and an
/// empty one, both states reachable, "Nothing waiting." shown for the empty case, and every
/// notice's role bar coloured through <see cref="Storyworthtelling.Maui.State.DogEarPalette"/>
/// rather than a hand-mixed hue.</para>
/// </summary>
public class WaitingPageTests
{
    [Fact]
    public void IsAConstructableContentPageTakingANoticeListAndAnOptionalSubtitle()
    {
        var source = Source();

        Assert.Contains("class WaitingPage : ContentPage", source, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"public WaitingPage\(IReadOnlyList<DogEarNotice> notices,\s*string\?\s*subtitle\s*=\s*null"),
            source);
    }

    /// <summary>
    /// Both states the constructor must handle without throwing — a non-empty list (real rows) and
    /// an empty one (the "Nothing waiting." branch) — are reachable in the source as an explicit
    /// <c>notices.Count == 0</c> branch, rather than one path silently falling through to the other.
    /// </summary>
    [Fact]
    public void HandlesBothAnEmptyAndANonEmptyNoticeListExplicitly()
    {
        var source = Source();

        Assert.Contains("notices.Count == 0", source, StringComparison.Ordinal);
        Assert.Contains("else", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsNothingWaitingTextForTheEmptyCase()
    {
        var source = Source();

        Assert.Contains("\"Nothing waiting.\"", source, StringComparison.Ordinal);
    }

    /// <summary>One fold, no count (§13): "Nothing waiting." is a plain literal, not an interpolated
    /// string with a count spliced in — a numeral there would be exactly the count this rule forbids.</summary>
    [Fact]
    public void TheEmptyStateTextIsAPlainLiteralNotAnInterpolatedCount()
    {
        Assert.Contains("NothingWaiting = \"Nothing waiting.\";", Source(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A notice's role bar reuses <see cref="Storyworthtelling.Maui.State.DogEarPalette"/> rather
    /// than mixing its own colour — the same rule <see cref="DogEarPaletteTests"/> guards for the
    /// dog-ear corner itself, now for every row on the page it turns to.
    /// </summary>
    [Fact]
    public void EachRowsBarColourComesFromDogEarPalette()
    {
        Assert.Contains("DogEarPalette.TokenFor(notice.Hue)", Source(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A notice's action is a secondary button, never the page's primary style — mockup 17: "There's
    /// no pinned bar here, because nothing on the page is the primary action."
    /// </summary>
    [Fact]
    public void ANoticesActionUsesTheSecondaryButtonStyleOnlyWhenALabelIsGiven()
    {
        var source = Source();

        Assert.Contains("PaperResources.Token<Style>(\"SecondaryButton\")", source, StringComparison.Ordinal);
        Assert.Contains("IsNullOrWhiteSpace(notice.ActionLabel)", source, StringComparison.Ordinal);
    }

    /// <summary>The page, the view it hosts and the plan the view paints: R6 split the body out so an
    /// app with its own binder frame can place it inside that frame, and the decisions out so they can
    /// be tested, so the guards read the three files as one surface.</summary>
    private static string Source() => PageSource() + "\n" + ViewSource() + "\n" + PlanSource();

    private static string PlanSource() => File.ReadAllText(RepoFile("Storyworthtelling.Maui/State/WaitingPlan.cs"));

    private static string PageSource() => File.ReadAllText(RepoFile("Storyworthtelling.Maui/Views/WaitingPage.cs"));

    private static string ViewSource()
    {
        var path = RepoFile("Storyworthtelling.Maui/Views/WaitingView.cs");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    // ---- R6 / mockup 17: what the audit found missing from the page ----

    /// <summary>
    /// Finding 5: the page was a bare ContentPage under a NavigationPage, so the system bar drew above
    /// "Waiting". Every binder page in the suite hides it; the header is the header.
    /// </summary>
    [Fact]
    public void ThePageHidesTheSystemNavigationBar()
    {
        Assert.Contains("NavigationPage.SetHasNavigationBar(this, false)", PageSource(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Finding 5: the page snapshotted the notices at push time, so "Nothing waiting." could never
    /// appear while it was open. It now listens to the state, and lets go when it goes.
    /// </summary>
    [Fact]
    public void TheViewListensToTheStateWhileShowingAndLetsGoWhenItIsNot()
    {
        var view = ViewSource();

        Assert.Contains("NoticesChanged +=", view, StringComparison.Ordinal);
        Assert.Contains("NoticesChanged -=", view, StringComparison.Ordinal);

        var page = PageSource();
        Assert.Contains("OnAppearing", page, StringComparison.Ordinal);
        Assert.Contains("OnDisappearing", page, StringComparison.Ordinal);
        Assert.Contains(".Attach()", page, StringComparison.Ordinal);
        Assert.Contains(".Detach()", page, StringComparison.Ordinal);
    }

    /// <summary>Finding 1: each row prints its date, from the kit's own formatter.</summary>
    [Fact]
    public void EachRowPrintsItsDate()
    {
        Assert.Contains("NoticeDates", Source(), StringComparison.Ordinal);
        Assert.Contains("row.Date", Source(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Finding 5: the kit page stays generic. A caller with its own frame takes the view and puts it
    /// inside that frame; the kit never learns the app's area table.
    /// </summary>
    [Fact]
    public void TheBodyIsAViewAnAppCanPlaceInsideItsOwnFrame()
    {
        Assert.Contains("class WaitingView : ContentView", ViewSource(), StringComparison.Ordinal);
    }

    private static string RepoFile(string relative) =>
        Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
