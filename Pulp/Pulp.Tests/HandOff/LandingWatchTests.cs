using Pulp.Domain.HandOffs;
using static Pulp.Tests.HandOff.HandOffFixtures;

namespace Pulp.Tests.HandOff;

/// <summary>
/// Orphan audit §28: LandingCheck.Decide had no caller, so the D122 dog-ear could never show. The watch
/// holds each started hand-off and the sessions Flyleaf relays, and gives a hand-off back once its
/// verdict can no longer change: as soon as the title is seen playing, or when the window closes.
/// </summary>
public class LandingWatchTests
{
    private static PendingHandOff Pending() =>
        Planner().Plan(Request(Flix, Sam), At).Pending ?? throw new InvalidOperationException("The fixture should start.");

    private static PlaybackSession Session(string? title, TimeSpan after) =>
        new("com.example.flix", At + after, title, TimeSpan.FromMinutes(1));

    [Fact]
    public void TheTitleSeenPlayingSettlesAtOnceAsLanded()
    {
        var watch = new LandingWatch();
        watch.Watch(Pending(), "anything");

        watch.Saw(Session("Lantern Cove", TimeSpan.FromSeconds(20)));
        var settled = watch.Settle(TestCatalogue, At + TimeSpan.FromSeconds(30));

        Assert.Equal(Landing.Landed, Assert.Single(settled).Verdict.Landing);
        Assert.Empty(watch.Settle(TestCatalogue, At + TimeSpan.FromHours(1)));
    }

    [Fact]
    public void AnotherTitleWaitsForTheWindow_SinceATrailerCanPlayFirst()
    {
        var watch = new LandingWatch();
        watch.Watch(Pending(), "anything");
        watch.Saw(Session("A Trailer For Something Else", TimeSpan.FromSeconds(10)));

        Assert.Empty(watch.Settle(TestCatalogue, At + TimeSpan.FromSeconds(30)));

        var settled = watch.Settle(TestCatalogue, At + LandingCheck.Window + TimeSpan.FromSeconds(1));
        Assert.Equal(Landing.LandedElsewhere, Assert.Single(settled).Verdict.Landing);
    }

    [Fact]
    public void NoSessionAtAllIsNotSeenOnceTheWindowCloses()
    {
        var watch = new LandingWatch();
        watch.Watch(Pending(), "anything");

        Assert.Empty(watch.Settle(TestCatalogue, At + TimeSpan.FromMinutes(1)));
        Assert.Equal(Landing.NotSeen, Assert.Single(watch.Settle(TestCatalogue, At + LandingCheck.Window + TimeSpan.FromSeconds(1))).Verdict.Landing);
    }

    [Fact]
    public void TheTagComesBackWithItsVerdict()
    {
        var watch = new LandingWatch();
        watch.Watch(Pending(), "the title page");
        watch.Saw(Session("Lantern Cove", TimeSpan.FromSeconds(5)));

        Assert.Equal("the title page", Assert.Single(watch.Settle(TestCatalogue, At + TimeSpan.FromSeconds(6))).Tag);
    }

    [Fact]
    public void SessionsOlderThanEveryWatchedHandOffAreNotKept()
    {
        var watch = new LandingWatch();
        watch.Saw(Session("Lantern Cove", -TimeSpan.FromMinutes(10)));
        watch.Watch(Pending(), "anything");

        Assert.Equal(0, watch.SessionCount);
    }
}
