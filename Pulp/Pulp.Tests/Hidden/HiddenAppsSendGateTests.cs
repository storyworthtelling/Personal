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

        Assert.True(gate.ShouldSend([]));
    }

    [Fact]
    public void TheSameSetAgainIsNotSentWhateverTheOrderOrRepeats()
    {
        var gate = new HiddenAppsSendGate();
        Assert.True(gate.ShouldSend([Flix, Sample]));

        Assert.False(gate.ShouldSend([Sample, Flix, Sample]));
    }

    [Fact]
    public void AChangedSetIsSentAndUntickingEverythingIsSentToo()
    {
        var gate = new HiddenAppsSendGate();
        gate.ShouldSend([Flix, Sample]);

        Assert.True(gate.ShouldSend([Flix]));
        Assert.True(gate.ShouldSend([]));
        Assert.False(gate.ShouldSend([]));
    }

    [Fact]
    public void ASetBeingSentAgainAfterChangingBackIsSent()
    {
        var gate = new HiddenAppsSendGate();
        gate.ShouldSend([Flix]);
        gate.ShouldSend([]);

        Assert.True(gate.ShouldSend([Flix]));
    }

    [Fact]
    public void ASendThatDidNotGoOutCanBeForgottenSoTheNextCallTriesAgain()
    {
        var gate = new HiddenAppsSendGate();
        gate.ShouldSend([Flix]);

        gate.Forget();

        Assert.True(gate.ShouldSend([Flix]));
    }
}
