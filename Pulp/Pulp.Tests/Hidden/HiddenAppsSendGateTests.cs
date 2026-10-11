using Pulp.Domain.Core;
using Pulp.Domain.Hidden;

namespace Pulp.Tests.Hidden;

/// <summary>
/// When Pulp re-sends the hidden set to Flyleaf (G17 wiring): once per process, then only when the
/// ticked set is a different set. Order and repeats are not a difference; the empty set is a set.
/// </summary>
public class HiddenAppsSendGateTests
{
    private static readonly ServiceId Flix = new("example-flix");
    private static readonly ServiceId Sample = new("sample-stream");

    [Fact]
    public void TheFirstCallAlwaysSendsEvenWithNothingTicked()
    {
        var gate = new HiddenAppsSendGate();

        Assert.True(gate.ShouldSend([], HiddenSendCause.Edit));
    }

    [Fact]
    public void TheSameSetAgainIsNotSentWhateverTheOrderOrRepeats()
    {
        var gate = new HiddenAppsSendGate();
        Assert.True(gate.ShouldSend([Flix, Sample], HiddenSendCause.Edit));

        Assert.False(gate.ShouldSend([Sample, Flix, Sample], HiddenSendCause.Edit));
    }

    [Fact]
    public void AChangedSetIsSentAndUntickingEverythingIsSentToo()
    {
        var gate = new HiddenAppsSendGate();
        gate.ShouldSend([Flix, Sample], HiddenSendCause.Edit);

        Assert.True(gate.ShouldSend([Flix], HiddenSendCause.Edit));
        Assert.True(gate.ShouldSend([], HiddenSendCause.Edit));
        Assert.False(gate.ShouldSend([], HiddenSendCause.Edit));
    }

    [Fact]
    public void ASetBeingSentAgainAfterChangingBackIsSent()
    {
        var gate = new HiddenAppsSendGate();
        gate.ShouldSend([Flix], HiddenSendCause.Edit);
        gate.ShouldSend([], HiddenSendCause.Edit);

        Assert.True(gate.ShouldSend([Flix], HiddenSendCause.Edit));
    }

    [Fact]
    public void ASendThatDidNotGoOutCanBeForgottenSoTheNextCallTriesAgain()
    {
        var gate = new HiddenAppsSendGate();
        gate.ShouldSend([Flix], HiddenSendCause.Edit);

        gate.Forget();

        Assert.True(gate.ShouldSend([Flix], HiddenSendCause.Edit));
    }

    [Fact]
    public void AnEmptySetSeenOnLoadIsNeverSentBecauseAFreshInstallHasNotSyncedYet()
    {
        var gate = new HiddenAppsSendGate();

        Assert.False(gate.ShouldSend([], HiddenSendCause.Load));
        Assert.False(gate.ShouldSend([], HiddenSendCause.Load));
    }

    [Fact]
    public void AnEmptySetOnLoadDoesNotUseUpTheLaterRealSend()
    {
        var gate = new HiddenAppsSendGate();
        gate.ShouldSend([], HiddenSendCause.Load);

        Assert.True(gate.ShouldSend([Flix], HiddenSendCause.Load));
    }

    [Fact]
    public void AnEditThatEmptiesTheSetIsSentBecauseThatIsHowUntickingUnHides()
    {
        var gate = new HiddenAppsSendGate();
        gate.ShouldSend([Flix], HiddenSendCause.Load);

        Assert.True(gate.ShouldSend([], HiddenSendCause.Edit));
        Assert.False(gate.ShouldSend([], HiddenSendCause.Edit));
    }

    [Fact]
    public void AnEmptyLoadAfterAnEmptyingEditIsStillNotSent()
    {
        var gate = new HiddenAppsSendGate();
        gate.ShouldSend([Flix], HiddenSendCause.Load);
        gate.ShouldSend([], HiddenSendCause.Edit);

        Assert.False(gate.ShouldSend([], HiddenSendCause.Load));
    }
}
