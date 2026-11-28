using Watch.Domain.Dash;
using Watch.Domain.Design;
using Watch.Domain.Touch;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// A text arriving buzzes the wrist's two short taps (P14 B8; WD §7, A12, A35), once for each arrival Post tells it of, by
/// the haptic plan: overnight only a favourite's, during a set not until the rest. The same arrival told again, and one
/// that crossed too late to mean anything, stay still.
/// </summary>
public class TextArrivalsTests
{
    private static DashArrived Lee(int seconds = 0, bool favourite = false) => new(LeeKey, T0.AddSeconds(seconds), favourite);

    [Fact]
    public void ATextBuzzesTwoShortTaps()
    {
        var buzz = new TextArrivals().Heard(Lee(), T0.AddSeconds(3), overnight: false);

        Assert.Equal(HapticAction.Buzz, buzz.Decision.Action);
        Assert.Same(WatchTokens.Haptics.Text, buzz.Decision.Pattern);
        Assert.Equal(BuzzUsage.Notification, buzz.Usage);
    }

    [Fact]
    public void TheSameArrivalBuzzesOnce()
    {
        var arrivals = new TextArrivals();

        Assert.Equal(HapticAction.Buzz, arrivals.Heard(Lee(), T0, overnight: false).Decision.Action);
        Assert.Equal(HapticAction.Silent, arrivals.Heard(Lee(), T0.AddSeconds(5), overnight: false).Decision.Action);
    }

    [Fact]
    public void ANewerTextFromTheSamePersonBuzzesAgain()
    {
        var arrivals = new TextArrivals();
        arrivals.Heard(Lee(), T0, overnight: false);

        Assert.Equal(HapticAction.Buzz, arrivals.Heard(Lee(seconds: 40), T0.AddSeconds(41), overnight: false).Decision.Action);
    }

    [Fact]
    public void AnArrivalThatCrossedTooLateStaysStill()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), TextArrivals.Fresh);

        var arrivals = new TextArrivals();
        Assert.Equal(HapticAction.Silent, arrivals.Heard(Lee(), T0 + TextArrivals.Fresh + TimeSpan.FromSeconds(1), overnight: false).Decision.Action);
        Assert.Equal(HapticAction.Buzz, new TextArrivals().Heard(Lee(), T0 + TextArrivals.Fresh, overnight: false).Decision.Action);
    }

    [Fact]
    public void AnArrivalAheadOfTheWristsClockStillBuzzes()
    {
        // The phone's clock may run ahead of the watch's; a text from the future is still a text now.
        Assert.Equal(HapticAction.Buzz, new TextArrivals().Heard(Lee(seconds: 90), T0, overnight: false).Decision.Action);
    }

    [Fact]
    public void OvernightSomeoneElsesTextStaysStill()
    {
        var buzz = new TextArrivals().Heard(Lee(favourite: false), T0, overnight: true);

        Assert.Equal(HapticDecision.Silent, buzz.Decision);
    }

    [Fact]
    public void OvernightAFavouritesTextComesThroughAsSomeoneAsking()
    {
        // The watch's own Do Not Disturb mutes a notification's buzz; a favourite's text is let through as a request to
        // talk, which that Do Not Disturb lets through when it lets starred callers and repeat callers ring.
        var buzz = new TextArrivals().Heard(Lee(favourite: true), T0, overnight: true);

        Assert.Same(WatchTokens.Haptics.Text, buzz.Decision.Pattern);
        Assert.Equal(BuzzUsage.CommunicationRequest, buzz.Usage);
    }

    [Fact]
    public void ByDayAFavouriteIsAnyoneElse() =>
        Assert.Equal(BuzzUsage.Notification, new TextArrivals().Heard(Lee(favourite: true), T0, overnight: false).Usage);

    [Fact]
    public void DuringASetATextWaits() =>
        Assert.Equal(HapticDecision.Hold, new TextArrivals().Heard(Lee(), T0, overnight: false, SessionState.InSet).Decision);

    [Fact]
    public void TheMemoryIsBounded()
    {
        Assert.Equal(32, TextArrivals.Remembered);
        var arrivals = new TextArrivals();
        arrivals.Heard(Lee(), T0, overnight: false);
        for (var index = 1; index <= TextArrivals.Remembered; index++)
        {
            arrivals.Heard(new DashArrived(GroupKey, T0.AddMilliseconds(index), false), T0, overnight: false);
        }

        // The first has been let go, so it would buzz again: the cost of a bounded memory, a text told again past 32 others.
        Assert.Equal(HapticAction.Buzz, arrivals.Heard(Lee(), T0, overnight: false).Decision.Action);
    }

    [Fact]
    public void TheSharedOneIsOne() => Assert.Same(TextArrivals.Shared, TextArrivals.Shared);
}
