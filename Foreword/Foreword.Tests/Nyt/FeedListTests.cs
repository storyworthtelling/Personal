using Foreword.Domain.Brief;
using Xunit;

namespace Foreword.Tests.Nyt;

/// <summary>
/// Where the brief comes from (T5), and the rule that carries the whole screen.
///
/// <para><b>A feed is never unsubscribed on the owner's behalf, however long it fails.</b> It stays
/// on the list, still marked, until they remove it — because a feed disappearing on its own is
/// indistinguishable from a feed you forgot you added, and the owner would spend a morning
/// wondering which.</para>
///
/// <para>Each feed carries its own cap rather than there being one overall, so a noisy feed cannot
/// crowd out a quiet one and a quiet morning still prints everything.</para>
/// </summary>
public class FeedListTests
{
    private static readonly DateOnly Today = new(2026, 9, 11);

    private static Feed Working(string name, int cap = 5) =>
        new() { Name = name, Url = $"https://{name}/feed", Cap = cap };

    // ---- never unsubscribed ----

    /// <summary>
    /// Six mornings of 403 and it is still there. The Trentonian is the real case — bot-blocked at
    /// the CDN (F17) — and it is why local news was dropped rather than why the feed vanished.
    /// </summary>
    [Fact]
    public void AFeedThatKeepsRefusingStaysOnTheList()
    {
        var refused = Working("trentonian.com") with
        {
            FailingSince = Today.AddDays(-5),
            LastFailure = "403",
        };

        var after = FeedList.AfterAMorning([refused], failed: [refused.Url], Today);

        Assert.Single(after);
        Assert.Equal(refused.Url, after[0].Url);
    }

    /// <summary>
    /// And it says how long. "Refused the fetch — 403, six mornings running" is a sentence the owner
    /// can act on; a feed that is simply quiet is one they will not notice for a month.
    /// </summary>
    [Fact]
    public void AFailingFeedSaysWhatAndForHowLong()
    {
        var refused = Working("trentonian.com") with
        {
            FailingSince = Today.AddDays(-5),
            LastFailure = "403",
        };

        Assert.Equal("Refused the fetch — 403, six mornings running", refused.Status(Today));
    }

    [Fact]
    public void OneBadMorningIsSingular()
    {
        var refused = Working("ars.technica") with { FailingSince = Today, LastFailure = "timed out" };

        Assert.Equal("Refused the fetch — timed out, one morning", refused.Status(Today));
    }

    /// <summary>A working feed says what it is and what it is capped at, not that it is fine.</summary>
    [Fact]
    public void AWorkingFeedSaysItsShapeRatherThanItsHealth()
    {
        Assert.Equal("RSS · cap 5 a morning", Working("ars.technica").Status(Today));
    }

    /// <summary>
    /// A feed that starts working again is clean, with no scar. The point of recording the failure
    /// was to let the owner act on it; once it stops failing there is nothing to act on.
    /// </summary>
    [Fact]
    public void AFeedThatRecoversStopsSayingItFailed()
    {
        var refused = Working("ars.technica") with
        {
            FailingSince = Today.AddDays(-5),
            LastFailure = "403",
        };

        var after = FeedList.AfterAMorning([refused], failed: [], Today).Single();

        Assert.Null(after.FailingSince);
        Assert.Equal("RSS · cap 5 a morning", after.Status(Today));
    }

    /// <summary>
    /// The first failure records the date; later ones leave it alone, because the interesting number
    /// is how long it has been broken rather than when it last was.
    /// </summary>
    [Fact]
    public void TheFailingSinceDateIsSetOnceAndThenLeftAlone()
    {
        var feed = Working("ars.technica");

        var first = FeedList.AfterAMorning([feed], [feed.Url], Today).Single();
        var later = FeedList.AfterAMorning([first], [feed.Url], Today.AddDays(3)).Single();

        Assert.Equal(Today, first.FailingSince);
        Assert.Equal(Today, later.FailingSince);
    }

    // ---- proposed feeds ----

    /// <summary>
    /// A team proposed from a calendar subscription waits to be followed. A calendar named after a
    /// team is a good guess, not a decision — and a brief that started printing transactions for a
    /// team the owner merely watches would be putting words in their mouth.
    /// </summary>
    [Fact]
    public void AProposedFeedIsNotFetchedUntilItIsFollowed()
    {
        var proposed = Working("phillies") with { Proposed = "a calendar named “Phillies”" };

        Assert.False(proposed.IsActive);
        Assert.Contains("Proposed from", proposed.Status(Today), StringComparison.Ordinal);
        Assert.Empty(FeedList.ToFetch([proposed]));
    }

    [Fact]
    public void FollowingAProposedFeedMakesItOrdinary()
    {
        var followed = (Working("phillies") with { Proposed = "a calendar" }).Followed();

        Assert.True(followed.IsActive);
        Assert.Single(FeedList.ToFetch([followed]));
    }

    // ---- the caps ----

    /// <summary>
    /// A feed that keeps refusing is still fetched. Its cap is shown as zero on the screen because
    /// zero is what it has been yielding, but giving up on it would be the unsubscribe this screen
    /// refuses to do — just quieter.
    /// </summary>
    [Fact]
    public void AFailingFeedIsStillTried()
    {
        var refused = Working("trentonian.com") with
        {
            FailingSince = Today.AddDays(-30),
            LastFailure = "403",
        };

        Assert.Single(FeedList.ToFetch([refused]));
    }

    /// <summary>
    /// A cap of zero means the owner has muted it, which is different from it failing: they said so.
    /// Muted feeds are not fetched, and they stay on the list.
    /// </summary>
    [Fact]
    public void AMutedFeedIsNotFetchedButIsStillListed()
    {
        var muted = Working("ars.technica", cap: 0);

        Assert.Empty(FeedList.ToFetch([muted]));
        Assert.Equal("RSS · muted", muted.Status(Today));
    }
}
