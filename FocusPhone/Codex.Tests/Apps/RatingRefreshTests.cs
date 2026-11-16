using Codex.Domain.Apps;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Apps;

public class RatingRefreshTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static RatingBook Book(params (string Package, RatingEntry Entry)[] entries) =>
        new(entries.ToDictionary(e => e.Package, e => e.Entry));

    private static RatingEntry Rated(ContentRating rating, long version, TimeSpan age) =>
        new(rating, version, Now - age, RatingLookupOutcome.Rated);

    [Fact]
    public void AnAppNeverLookedUp_IsDue()
    {
        var due = RatingRefresh.Due([("com.example.notes", 1)], RatingBook.Empty, Now);

        Assert.Equal(["com.example.notes"], due);
    }

    [Fact]
    public void AFreshRatingForTheSameVersion_IsNotDue()
    {
        var book = Book(("com.example.notes", Rated(ContentRating.Teen, 5, TimeSpan.FromDays(3))));

        Assert.Empty(RatingRefresh.Due([("com.example.notes", 5)], book, Now));
    }

    [Fact]
    public void ANewVersion_IsDue_BecauseARatingCanChangeWithAnUpdate()
    {
        var book = Book(("com.example.notes", Rated(ContentRating.Teen, 5, TimeSpan.FromDays(3))));

        Assert.Equal(["com.example.notes"], RatingRefresh.Due([("com.example.notes", 6)], book, Now));
    }

    [Fact]
    public void AStaleRating_IsDue()
    {
        var book = Book(("com.example.notes", Rated(ContentRating.Teen, 5, RatingRefresh.Fresh)));

        Assert.Equal(["com.example.notes"], RatingRefresh.Due([("com.example.notes", 5)], book, Now));
    }

    [Fact]
    public void AFailedLookup_WaitsBeforeTryingAgain()
    {
        var failed = new RatingEntry(ContentRating.Unknown, 5, Now - TimeSpan.FromHours(2), RatingLookupOutcome.Failed);
        var book = Book(("com.example.notes", failed));

        Assert.Empty(RatingRefresh.Due([("com.example.notes", 6)], book, Now));
        Assert.Equal(["com.example.notes"],
            RatingRefresh.Due([("com.example.notes", 6)], book, Now + RatingRefresh.RetryAfterFailure));
    }

    [Fact]
    public void TheOwnersSuiteTheAllowlistAndPulpsServices_AreNeverLookedUp()
    {
        var installed = new List<(string, long)>
        {
            ("com.storyworthtelling.pulp", 1),
            (PolicyContext.GeminiPackage, 1),
            ("com.netflix.mediaclient", 1),
            ("com.example.notes", 1),
        };

        Assert.Equal(["com.example.notes"], RatingRefresh.Due(installed, RatingBook.Empty, Now));
    }

    [Fact]
    public void ARunIsCapped_NeverLookedUpFirst()
    {
        var installed = Enumerable.Range(0, RatingRefresh.PerRun + 5)
            .Select(i => ($"com.example.app{i}", 1L)).ToList();
        var book = Book(("com.example.app0", Rated(ContentRating.Teen, 1, RatingRefresh.Fresh)));

        var due = RatingRefresh.Due(installed, book, Now);

        Assert.Equal(RatingRefresh.PerRun, due.Count);
        Assert.DoesNotContain("com.example.app0", due);
    }

    [Fact]
    public void Record_ARatingReplacesTheEntry()
    {
        var book = RatingBook.Empty.Record("com.example.forum", 3,
            new PlayLookup(RatingLookupOutcome.Rated, ContentRating.Mature), Now);

        Assert.Equal(ContentRating.Mature, book.RatingOf("com.example.forum"));
        Assert.Equal(["com.example.forum"], book.MaturePackages());
    }

    [Theory]
    [InlineData(RatingLookupOutcome.Failed)]
    [InlineData(RatingLookupOutcome.Unreadable)]
    public void Record_AFailedLookupKeepsTheRatingItHad(RatingLookupOutcome outcome)
    {
        var book = RatingBook.Empty
            .Record("com.example.forum", 3, new PlayLookup(RatingLookupOutcome.Rated, ContentRating.Mature), Now)
            .Record("com.example.forum", 4, new PlayLookup(outcome, ContentRating.Unknown), Now);

        Assert.Equal(ContentRating.Mature, book.RatingOf("com.example.forum"));
        Assert.Equal(RatingLookupOutcome.Failed, book.Entries["com.example.forum"].Outcome);
    }

    [Fact]
    public void Record_AnAppNotOnPlay_IsUnknown()
    {
        var book = RatingBook.Empty.Record("com.example.sideloaded", 1,
            new PlayLookup(RatingLookupOutcome.NotListed, ContentRating.Unknown), Now);

        Assert.Equal(ContentRating.Unknown, book.RatingOf("com.example.sideloaded"));
        Assert.Empty(book.MaturePackages());
    }

    [Fact]
    public void RetainOnly_ForgetsUninstalledApps()
    {
        var book = RatingBook.Empty
            .Record("com.example.a", 1, new PlayLookup(RatingLookupOutcome.Rated, ContentRating.Teen), Now)
            .Record("com.example.b", 1, new PlayLookup(RatingLookupOutcome.Rated, ContentRating.Mature), Now)
            .RetainOnly(["com.example.a"]);

        Assert.Equal(["com.example.a"], book.Entries.Keys);
    }
}
