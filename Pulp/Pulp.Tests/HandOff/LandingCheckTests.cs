using Pulp.Domain.HandOffs;
using static Pulp.Tests.HandOff.HandOffFixtures;

namespace Pulp.Tests.HandOff;

/// <summary>
/// D122 — did the deep link land on the title, or on the app's home or another title? Told by the
/// playing app's media session (finding §2.4), which arrives later through Flyleaf's relay or Pulp's own
/// listener (Release F), so the check takes the sessions as input. Where the app publishes no title the
/// check says so — Unknown — rather than guess; only a session for another title is a mis-landing, and
/// no session at all once the window closes is NotSeen, which the dog-ear mentions quietly (D286).
/// </summary>
public class LandingCheckTests
{
    private static PendingHandOff Pending()
    {
        var plan = Planner().Plan(Request(Flix, Sam), At);
        return plan.Pending ?? throw new InvalidOperationException("The fixture's adult request should start.");
    }

    private static readonly DateTimeOffset Later = At + LandingCheck.Window + TimeSpan.FromMinutes(1);

    private static PlaybackSession Session(string package, string? title, TimeSpan after) =>
        new(package, At + after, title, TimeSpan.FromMinutes(1));

    [Fact]
    public void ASessionForTheTitleOnTheServicesAppIsLanded()
    {
        var verdict = LandingCheck.Decide(Pending(), [Session("com.example.flix", "Lantern Cove", TimeSpan.FromSeconds(20))], TestCatalogue, Later);

        Assert.Equal(new LandingVerdict(Landing.Landed, LandingReason.TitleMatched), verdict);
    }

    [Theory]
    [InlineData("LANTERN COVE")]
    [InlineData("Lantern Cove: The Tide Bell")]
    [InlineData("S1 E3 · Lantern  Cove")]
    public void TheTitleMatchesWithoutCaseOrDecoration(string published)
    {
        var verdict = LandingCheck.Decide(Pending(), [Session("com.example.flix", published, TimeSpan.FromSeconds(20))], TestCatalogue, Later);

        Assert.Equal(Landing.Landed, verdict.Landing);
    }

    [Fact]
    public void AWordInsideAnotherWordIsNotAMatch()
    {
        var verdict = LandingCheck.Decide(Pending(), [Session("com.example.flix", "Lantern Coves of the North", TimeSpan.FromSeconds(20))], TestCatalogue, Later);

        Assert.Equal(new LandingVerdict(Landing.LandedElsewhere, LandingReason.OtherTitlePlaying), verdict);
    }

    [Fact]
    public void AnotherServicePackageOfTheSameServiceCounts()
    {
        var verdict = LandingCheck.Decide(Pending(), [Session("com.example.flix.legacy", "Lantern Cove", TimeSpan.FromSeconds(20))], TestCatalogue, Later);

        Assert.Equal(Landing.Landed, verdict.Landing);
    }

    [Fact]
    public void AnotherTitlePlayingOnTheServiceIsAMisLanding()
    {
        var verdict = LandingCheck.Decide(Pending(), [Session("com.example.flix", "Harbour Lights", TimeSpan.FromSeconds(30))], TestCatalogue, Later);

        Assert.Equal(new LandingVerdict(Landing.LandedElsewhere, LandingReason.OtherTitlePlaying), verdict);
    }

    [Fact]
    public void TheTitleWinsOverAnEarlierWrongOne()
    {
        // The app auto-played a trailer on its home before the viewer reached the title.
        PlaybackSession[] sessions =
        [
            Session("com.example.flix", "Harbour Lights", TimeSpan.FromSeconds(10)),
            Session("com.example.flix", "Lantern Cove", TimeSpan.FromSeconds(50)),
        ];

        Assert.Equal(Landing.Landed, LandingCheck.Decide(Pending(), sessions, TestCatalogue, Later).Landing);
    }

    [Fact]
    public void ASessionWithNoTitleCannotTellAndSaysSo()
    {
        var verdict = LandingCheck.Decide(Pending(), [Session("com.example.flix", null, TimeSpan.FromSeconds(20))], TestCatalogue, Later);

        Assert.Equal(new LandingVerdict(Landing.Unknown, LandingReason.TitleNotPublished), verdict);
    }

    [Fact]
    public void NoSessionAtAllOnceTheWindowClosesIsNotSeen()
    {
        // D286: silence from the service's app is worth a quiet "may not have landed" in the dog-ear —
        // the owner chose a possible false alarm over a missed wrong landing.
        var verdict = LandingCheck.Decide(Pending(), [], TestCatalogue, Later);

        Assert.Equal(new LandingVerdict(Landing.NotSeen, LandingReason.NoSessionSeen), verdict);
    }

    [Fact]
    public void BeforeTheWindowClosesWithNothingSeenItIsStillWaiting()
    {
        var verdict = LandingCheck.Decide(Pending(), [], TestCatalogue, At + TimeSpan.FromSeconds(30));

        Assert.Equal(new LandingVerdict(Landing.Unknown, LandingReason.StillWaiting), verdict);
    }

    [Fact]
    public void SessionsOutsideTheWindowOrFromOtherAppsAreIgnored()
    {
        PlaybackSession[] sessions =
        [
            Session("com.example.flix", "Harbour Lights", TimeSpan.FromMinutes(-5)),
            Session("com.example.flix", "Harbour Lights", LandingCheck.Window + TimeSpan.FromSeconds(1)),
            Session("com.example.sample", "Harbour Lights", TimeSpan.FromSeconds(20)),
            Session("com.example.unknown", "Harbour Lights", TimeSpan.FromSeconds(20)),
        ];

        var verdict = LandingCheck.Decide(Pending(), sessions, TestCatalogue, Later);

        Assert.Equal(new LandingVerdict(Landing.NotSeen, LandingReason.NoSessionSeen), verdict);
    }

    [Fact]
    public void WithNoNameToCompareATitledSessionIsUnknown()
    {
        var plan = Planner().Plan(new HandOffRequest([Sam], Facts(Flix), Link(Flix), []), At);

        var verdict = LandingCheck.Decide(plan.Pending!, [Session("com.example.flix", "Harbour Lights", TimeSpan.FromSeconds(20))], TestCatalogue, Later);

        Assert.Equal(new LandingVerdict(Landing.Unknown, LandingReason.NoNameToMatch), verdict);
    }
}
