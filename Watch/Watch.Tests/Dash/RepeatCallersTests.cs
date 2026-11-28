using System.Text;
using Watch.Domain.Dash;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// Unknown numbers ring only if they call twice within minutes (P14 B8, W-5.1.9; A12). The platform shows Dash's call
/// screening only calls from numbers not in the watch's contacts; a first call shows without ringing, the same caller
/// again inside the window rings. The caller is remembered as a keyed digest made with a secret that never leaves memory,
/// so no number is kept on the watch.
/// </summary>
public class RepeatCallersTests
{
    private static readonly byte[] Secret = [.. Enumerable.Range(1, 32).Select(value => (byte)value)];

    private static string Caller(string number) => CallerKey.Of(number, Secret);

    [Fact]
    public void AFirstCallShowsWithoutRinging() =>
        Assert.Equal(RingVerdict.Silence, new RepeatCallers().Heard(Caller("+1 212-555-0101"), T0));

    [Fact]
    public void TheSameCallerAgainWithinTheWindowRings()
    {
        var callers = new RepeatCallers();
        callers.Heard(Caller("+1 212-555-0101"), T0);

        Assert.Equal(RingVerdict.Ring, callers.Heard(Caller("+1 212-555-0101"), T0.AddMinutes(4)));
    }

    [Fact]
    public void TheWindowIsAndroidsOwn()
    {
        // The watch's Do Not Disturb lets a repeat caller ring within fifteen minutes; the day's rule says the same.
        Assert.Equal(TimeSpan.FromMinutes(15), RepeatCallers.Window);

        var callers = new RepeatCallers();
        callers.Heard(Caller("+1 212-555-0101"), T0);
        Assert.Equal(RingVerdict.Ring, callers.Heard(Caller("+1 212-555-0101"), T0 + RepeatCallers.Window));
    }

    [Fact]
    public void AfterTheWindowItIsAFirstCallAgain()
    {
        var callers = new RepeatCallers();
        callers.Heard(Caller("+1 212-555-0101"), T0);

        Assert.Equal(RingVerdict.Silence, callers.Heard(Caller("+1 212-555-0101"), T0 + RepeatCallers.Window + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AThirdCallStillRings()
    {
        var callers = new RepeatCallers();
        callers.Heard(Caller("+1 212-555-0101"), T0);
        callers.Heard(Caller("+1 212-555-0101"), T0.AddMinutes(3));

        Assert.Equal(RingVerdict.Ring, callers.Heard(Caller("+1 212-555-0101"), T0.AddMinutes(6)));
    }

    [Fact]
    public void AnotherCallerIsNoRepeat()
    {
        var callers = new RepeatCallers();
        callers.Heard(Caller("+1 212-555-0101"), T0);

        Assert.Equal(RingVerdict.Silence, callers.Heard(Caller("+1 212-555-0102"), T0.AddMinutes(1)));
    }

    [Theory]
    [InlineData("+1 (212) 555-0101")]
    [InlineData("212.555.0101")]
    [InlineData("12125550101")]
    [InlineData("tel:+12125550101")]
    public void ANumberSpelledAnotherWayIsTheSameCaller(string spelling) =>
        Assert.Equal(Caller("2125550101"), Caller(spelling));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Private")]
    public void ACallWithNoNumberIsWithheld(string? number) => Assert.Equal(CallerKey.Withheld, CallerKey.Of(number, Secret));

    [Fact]
    public void TwoWithheldCallsWithinTheWindowRing()
    {
        // A withheld caller can't be told from another, so two withheld calls close together count as one calling twice.
        var callers = new RepeatCallers();
        callers.Heard(CallerKey.Withheld, T0);

        Assert.Equal(RingVerdict.Ring, callers.Heard(CallerKey.Withheld, T0.AddMinutes(2)));
    }

    [Fact]
    public void TheKeyHoldsNoNumber()
    {
        var key = Caller("+1 212-555-0101");

        Assert.Matches("^k[0-9a-f]{32}$", key);
        Assert.DoesNotContain("2125550101", key, StringComparison.Ordinal);
        Assert.DoesNotContain("5550101", key, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherSecretIsAnotherKey()
    {
        var other = Encoding.UTF8.GetBytes("another secret, thirty-two bytes");

        Assert.NotEqual(Caller("+1 212-555-0101"), CallerKey.Of("+1 212-555-0101", other));
    }

    [Fact]
    public void TheSealIsWhatKeysTheCaller()
    {
        // On the watch the seal is a key the platform's keystore holds and won't give out, so a kept digest can't be
        // turned back into a number anywhere else; what it is sealed with is the head's to choose.
        var sealedWith = new List<byte[]>();
        var key = CallerKey.Of("+1 212-555-0101", digits => { sealedWith.Add(digits); return Secret; });

        Assert.Equal("2125550101", Encoding.ASCII.GetString(Assert.Single(sealedWith)));
        Assert.Equal("k" + Convert.ToHexString(Secret)[..32].ToLowerInvariant(), key);
    }

    [Fact]
    public void AWithheldCallIsNeverSealed()
    {
        var sealedWith = new List<byte[]>();

        Assert.Equal(CallerKey.Withheld, CallerKey.Of(null, digits => { sealedWith.Add(digits); return Secret; }));
        Assert.Empty(sealedWith);
    }

    // ---- across a restart: the process can die between two calls minutes apart ----

    [Fact]
    public void TheCallersCrossARestart()
    {
        var callers = new RepeatCallers();
        callers.Heard(Caller("+1 212-555-0101"), T0);

        var record = callers.Record(T0.AddMinutes(1));

        Assert.Equal(RingVerdict.Ring, RepeatCallers.From(record, T0.AddMinutes(4)).Heard(Caller("+1 212-555-0101"), T0.AddMinutes(4)));
    }

    [Fact]
    public void ACallerPastTheWindowIsNotKept()
    {
        var callers = new RepeatCallers();
        callers.Heard(Caller("+1 212-555-0101"), T0);

        Assert.Equal(string.Empty, callers.Record(T0 + RepeatCallers.Window + TimeSpan.FromSeconds(1)));
        Assert.Equal(RingVerdict.Silence,
            RepeatCallers.From(callers.Record(T0.AddMinutes(1)), T0.AddMinutes(20)).Heard(Caller("+1 212-555-0101"), T0.AddMinutes(20)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a record")]
    [InlineData("k0123\t1")]
    [InlineData("+12125550101\t1795364400000")]
    [InlineData("kffffffffffffffffffffffffffffffff\tsoon")]
    public void ADamagedRecordRemembersNobody(string? record)
    {
        var callers = RepeatCallers.From(record, T0);

        Assert.Equal(string.Empty, callers.Record(T0));
        Assert.Equal(RingVerdict.Silence, callers.Heard("kffffffffffffffffffffffffffffffff", T0));
    }

    [Fact]
    public void TheRecordHoldsKeysAndMomentsOnly()
    {
        var callers = new RepeatCallers();
        callers.Heard(Caller("+1 212-555-0101"), T0);
        callers.Heard(CallerKey.Withheld, T0.AddMinutes(1));

        var record = callers.Record(T0.AddMinutes(2));

        Assert.All(record.Split('\n'), line => Assert.Matches(@"^(k[0-9a-f]{32}|withheld)\t\d+$", line));
        Assert.DoesNotContain("5550101", record, StringComparison.Ordinal);
    }

    [Fact]
    public void AtMostTheRememberedCrossARestart()
    {
        var callers = new RepeatCallers();
        for (var index = 0; index < RepeatCallers.Remembered + 5; index++)
        {
            callers.Heard(Caller($"+1 212-555-{index:0000}"), T0.AddSeconds(index));
        }

        Assert.Equal(RepeatCallers.Remembered, callers.Record(T0.AddMinutes(1)).Split('\n').Length);
    }

    [Fact]
    public void TheMemoryIsBounded()
    {
        Assert.Equal(32, RepeatCallers.Remembered);
        var callers = new RepeatCallers();
        callers.Heard(Caller("+1 212-555-0101"), T0);
        for (var index = 0; index < RepeatCallers.Remembered; index++)
        {
            callers.Heard(Caller($"+1 212-555-{index:0000}"), T0.AddSeconds(index + 1));
        }

        // The first has been let go: past 32 other callers within minutes, a second call shows without ringing.
        Assert.Equal(RingVerdict.Silence, callers.Heard(Caller("+1 212-555-0101"), T0.AddMinutes(1)));
    }
}
