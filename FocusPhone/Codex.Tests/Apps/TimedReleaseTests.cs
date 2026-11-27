using Codex.Domain.Apps;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Apps;

/// <summary>
/// The Entertainment block's timed release (Flyleaf's half): what Pulp may ask Flyleaf to lift, how long a
/// release may run, and which windows are still open. Pure; the head is covered by the contract guard.
/// </summary>
public class TimedReleaseTests
{
    private const string Netflix = "com.netflix.mediaclient";
    private static readonly Uri Link = new("https://www.netflix.com/title/harbor-lights");
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 20, 0, 0, TimeSpan.Zero);

    private static readonly HiddenApps TickedNetflix = HiddenApps.Of([Netflix]);

    private static LockedOpenRefusal Decide(string package, HiddenApps? hidden = null, IEnumerable<string>? blocked = null, Uri? link = null) =>
        TimedReleaseGate.Decide(new LockedOpenRequest(package, link ?? Link), hidden ?? TickedNetflix, new HashSet<string>(blocked ?? []));

    [Fact]
    public void ATickedStreamingServiceMayBeReleased()
    {
        Assert.Equal(LockedOpenRefusal.None, Decide(Netflix));
    }

    [Fact]
    public void AnAppThatIsNotAStreamingServiceMayNotBeReleased()
    {
        // The same signed-sender rule as the locked open: any Entertainment app is not thereby releasable.
        var hidden = HiddenApps.Of(["com.example.clips"]);

        Assert.Equal(LockedOpenRefusal.NotStreamingService,
            Decide("com.example.clips", hidden, link: new Uri("https://www.netflix.com/title/x")));
    }

    [Fact]
    public void AServiceTheHouseholdDidNotTickMayNotBeReleased()
    {
        Assert.Equal(LockedOpenRefusal.NotHidden, Decide(Netflix, hidden: HiddenApps.Of([])));
    }

    [Fact]
    public void AServiceOnAForeignLinkMayNotBeReleased()
    {
        Assert.Equal(LockedOpenRefusal.LinkHostNotAllowed, Decide(Netflix, link: new Uri("https://www.example.com/title/x")));
    }

    [Fact]
    public void AServiceTheOwnerBlockedMayNotBeReleased()
    {
        Assert.Equal(LockedOpenRefusal.OwnerBlocked, Decide(Netflix, blocked: [Netflix]));
    }

    [Fact]
    public void TheOwnerNamespaceMayNeverBeReleased()
    {
        Assert.Equal(LockedOpenRefusal.OwnerNamespace,
            Decide(PolicyContext.PulpPackage, hidden: HiddenApps.Of([PolicyContext.PulpPackage])));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("150", true)]
    [InlineData("360", true)]
    [InlineData("0", false)]
    [InlineData("361", false)]
    [InlineData("-5", false)]
    [InlineData("ninety", false)]
    [InlineData(null, false)]
    public void TheWindowIsRefusedOutsideItsRange(string? text, bool accepted)
    {
        Assert.Equal(accepted, TimedReleaseGate.TryReadMinutes(text, out _));
    }

    [Fact]
    public void AnOpenWindowIsActiveUntilItsEnd()
    {
        var windows = TimedReleases.With(new Dictionary<string, long>(), Netflix, TimedReleases.Until(Now, 90));

        Assert.Contains(Netflix, TimedReleases.Active(windows, Now.AddMinutes(89)));
        Assert.DoesNotContain(Netflix, TimedReleases.Active(windows, Now.AddMinutes(90)));
    }

    [Fact]
    public void AClosedWindowIsNotTrustedEvenIfItsAlarmWasLost()
    {
        var windows = TimedReleases.With(new Dictionary<string, long>(), Netflix, TimedReleases.Until(Now, 10));

        Assert.Empty(TimedReleases.Active(windows, Now.AddHours(3)));
    }

    [Fact]
    public void TheNextExpiryIsTheEarliestOpenWindowEnd()
    {
        var windows = TimedReleases.With(
            TimedReleases.With(new Dictionary<string, long>(), Netflix, TimedReleases.Until(Now, 120)),
            "com.hulu.plus", TimedReleases.Until(Now, 30));

        Assert.Equal(Now.AddMinutes(30), TimedReleases.NextExpiry(windows, Now));
    }

    [Fact]
    public void NothingOpenHasNoNextExpiry()
    {
        Assert.Null(TimedReleases.NextExpiry(new Dictionary<string, long>(), Now));
    }

    [Fact]
    public void ASecondReleaseOfTheSameServiceReplacesItsWindow()
    {
        var first = TimedReleases.With(new Dictionary<string, long>(), Netflix, TimedReleases.Until(Now, 30));
        var second = TimedReleases.With(first, Netflix, TimedReleases.Until(Now, 120));

        Assert.Equal(Now.AddMinutes(120), TimedReleases.NextExpiry(second, Now));
        Assert.Single(second);
    }
}
