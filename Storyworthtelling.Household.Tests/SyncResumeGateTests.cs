using Storyworthtelling.Household;

namespace Storyworthtelling.Household.Tests;

/// <summary>A pass ran only at launch and after an edit; coming back to the app asks for one, at most once a minute.</summary>
public class SyncResumeGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheFirstResumeAsks() => Assert.True(SyncResumeGate.ShouldAsk(null, Now));

    [Fact]
    public void AResumeWithinAMinuteOfTheLastAskDoesNot() =>
        Assert.False(SyncResumeGate.ShouldAsk(Now.AddSeconds(-59), Now));

    [Fact]
    public void AResumeAMinuteLaterAsksAgain() =>
        Assert.True(SyncResumeGate.ShouldAsk(Now.AddSeconds(-60), Now));

    [Fact]
    public void AClockSetBackDoesNotLockAskingOutForever() =>
        Assert.True(SyncResumeGate.ShouldAsk(Now.AddHours(3), Now));
}
