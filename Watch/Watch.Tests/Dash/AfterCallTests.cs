using Watch.Domain.Dash;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// The call note after a system call (W-5.1.5, A13; mock: After the call). The watch's own Phone app rings and takes the
/// call; Dash hears only the platform's call state (off hook, then idle) and nothing about who it was, keeps the end in
/// memory, and offers the note the next time it opens, once. Which call it was, and whose, is Post's to say.
/// </summary>
public class AfterCallTests
{
    private static AfterCall Heard(params (CallState State, int Second)[] states)
    {
        var watch = new AfterCall();
        foreach (var (state, second) in states)
        {
            watch.Heard(state, T0.AddSeconds(second));
        }
        return watch;
    }

    [Fact]
    public void AnAnsweredCallEndsWhenItGoesIdle()
    {
        var watch = new AfterCall();

        Assert.Null(watch.Heard(CallState.Ringing, T0));
        Assert.Null(watch.Heard(CallState.OffHook, T0.AddSeconds(10)));
        var end = watch.Heard(CallState.Idle, T0.AddSeconds(370));

        Assert.Equal(new CallEnd(T0.AddSeconds(370), T0.AddSeconds(10)), end);
        Assert.Equal(TimeSpan.FromMinutes(6), end!.Length);
    }

    [Fact]
    public void AMissedCallIsNoEnd()
    {
        // It rang and stopped: nobody was on it, so there is nothing to note.
        var watch = new AfterCall();
        watch.Heard(CallState.Ringing, T0);

        Assert.Null(watch.Heard(CallState.Idle, T0.AddSeconds(30)));
        Assert.Null(watch.Due(T0.AddSeconds(40)));
    }

    [Fact]
    public void IdleAfterIdleIsNoEnd()
    {
        var watch = Heard((CallState.OffHook, 0), (CallState.Idle, 60));

        Assert.Null(watch.Heard(CallState.Idle, T0.AddSeconds(90)));
    }

    [Fact]
    public void AFreshProcessThatHearsIdleHasHeardAnEnd()
    {
        // The process can die during a long call; the idle that follows comes to a new one, which heard nothing before.
        var end = new AfterCall().Heard(CallState.Idle, T0);

        Assert.Equal(new CallEnd(T0, null), end);
        Assert.Null(end!.Length);
    }

    [Fact]
    public void ACallIsTimedFromItsFirstOffHook()
    {
        // Call waiting or a held call can say off hook again; the call began at the first.
        var watch = Heard((CallState.OffHook, 0), (CallState.OffHook, 30));

        Assert.Equal(TimeSpan.FromSeconds(90), watch.Heard(CallState.Idle, T0.AddSeconds(90))!.Length);
    }

    [Fact]
    public void AnEndIsOfferedForHalfAnHour()
    {
        var watch = Heard((CallState.OffHook, 0), (CallState.Idle, 60));
        var ended = T0.AddSeconds(60);

        Assert.Equal(TimeSpan.FromMinutes(30), AfterCall.OfferFor);
        Assert.NotNull(watch.Due(ended));
        Assert.NotNull(watch.Due(ended + AfterCall.OfferFor));
        Assert.Null(watch.Due(ended + AfterCall.OfferFor + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ANewerCallTakesTheOffersPlace()
    {
        // One offer, like the phone's one slot: the call that ended last is the one asked about.
        var watch = Heard((CallState.OffHook, 0), (CallState.Idle, 60), (CallState.OffHook, 120), (CallState.Idle, 300));

        Assert.Equal(T0.AddSeconds(300), watch.Due(T0.AddSeconds(310))!.Ended);
    }

    [Fact]
    public void ASettledOfferIsNotDueAgain()
    {
        var watch = Heard((CallState.OffHook, 0), (CallState.Idle, 60));
        var end = watch.Due(T0.AddSeconds(70))!;

        watch.Settle(end);

        Assert.Null(watch.Due(T0.AddSeconds(80)));
    }

    [Fact]
    public void SettlingAnOlderEndKeepsANewerOne()
    {
        // The answer about one call came back after another had ended: the newer one is still to be asked about.
        var watch = Heard((CallState.OffHook, 0), (CallState.Idle, 60));
        var older = watch.Due(T0.AddSeconds(61))!;
        watch.Heard(CallState.OffHook, T0.AddSeconds(120));
        watch.Heard(CallState.Idle, T0.AddSeconds(300));

        watch.Settle(older);

        Assert.Equal(T0.AddSeconds(300), watch.Due(T0.AddSeconds(310))!.Ended);
    }

    [Fact]
    public void TheLengthIsThePhonesTalkWhenItHasOne()
    {
        // The wrist's own timing runs from off hook, which for a call it placed includes the ring; Post's log is the talk.
        var end = new CallEnd(T0, T0.AddMinutes(-7));

        Assert.Equal(TimeSpan.FromMinutes(6), AfterCall.LengthOf((DashCall)Messages["call"], end));
        Assert.Equal(TimeSpan.FromMinutes(7), AfterCall.LengthOf((DashCall)Messages["call with no name"], end));
        Assert.Null(AfterCall.LengthOf((DashCall)Messages["call with no name"], new CallEnd(T0, null)));
    }

    [Fact]
    public void TheSharedOneIsOne()
    {
        // The receiver and People waiting are told about the same calls.
        Assert.Same(AfterCall.Shared, AfterCall.Shared);
    }
}
