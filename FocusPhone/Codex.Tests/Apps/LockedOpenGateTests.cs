using Codex.Domain.Apps;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Apps;

/// <summary>
/// D296: Flyleaf lock-tasks an app only when the request names a streaming service Pulp ticked. A
/// signed sender is still a sender: the hidden set alone is not trust (any suite app can write it),
/// so the package must also be one the curated streaming catalogue names, and never an owner
/// package, an app the plan hard-suspends, or one the owner blocked.
/// </summary>
public class LockedOpenGateTests
{
    private const string Netflix = "com.netflix.mediaclient";
    private static readonly Uri Link = new("https://www.netflix.com/title/harbor-lights");

    private static readonly HiddenApps TickedNetflix = HiddenApps.Of([Netflix]);

    private static LockedOpenRefusal Decide(
        string package,
        HiddenApps? hidden = null,
        IEnumerable<string>? blocked = null,
        IEnumerable<string>? policySuspended = null,
        Uri? link = null) =>
        LockedOpenGate.Decide(
            new LockedOpenRequest(package, link ?? Link),
            hidden ?? TickedNetflix,
            new HashSet<string>(blocked ?? []),
            new HashSet<string>(policySuspended ?? []));

    [Fact]
    public void ATickedCataloguedServiceIsAllowed()
    {
        Assert.Equal(LockedOpenRefusal.None, Decide(Netflix));
    }

    [Fact]
    public void ASignedSenderCannotLockTaskAnAppByHidingItFirst()
    {
        // The hidden set is writable by any signed sibling; the catalogue is what stops this.
        Assert.Equal(
            LockedOpenRefusal.NotStreamingService,
            Decide("com.android.chrome", HiddenApps.Of(["com.android.chrome"])));
    }

    [Fact]
    public void ACataloguedServiceThatWasNotTickedIsRefused()
    {
        Assert.Equal(LockedOpenRefusal.NotHidden, Decide(Netflix, HiddenApps.None));
        Assert.Equal(LockedOpenRefusal.NotHidden, Decide(Netflix, HiddenApps.Of(["com.hulu.plus"])));
    }

    [Theory]
    [InlineData("com.storyworthtelling.focusphone.communication")]
    [InlineData("com.storyworthtelling.focusphone.launcher")]
    [InlineData("com.storyworthtelling.pulp")]
    public void AnOwnerPackageIsAlwaysRefused(string package)
    {
        Assert.Equal(LockedOpenRefusal.OwnerNamespace, Decide(package, HiddenApps.Of([package])));
    }

    [Fact]
    public void AnAppThePolicyHardSuspendsIsNeverReleasedByARequest()
    {
        Assert.Equal(LockedOpenRefusal.PolicySuspended, Decide(Netflix, policySuspended: [Netflix]));
    }

    [Fact]
    public void AnAppTheOwnerBlockedStaysBlocked()
    {
        Assert.Equal(LockedOpenRefusal.OwnerBlocked, Decide(Netflix, blocked: [Netflix]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("netflix")]
    [InlineData("com..bad")]
    public void ABadPackageShapeIsRefused(string package)
    {
        Assert.Equal(LockedOpenRefusal.Unreadable, Decide(package));
    }

    [Fact]
    public void EveryCataloguedPackageHasTheShapeOfAPackageAndNoneIsOwnedByTheOwner()
    {
        Assert.NotEmpty(LockedOpenCatalogue.Packages);
        foreach (var package in LockedOpenCatalogue.Packages)
        {
            Assert.DoesNotContain(package, HardBlock.CuratedPackages);
            Assert.False(package.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal), package);
            var host = LockedOpenCatalogue.LinkDomains[package].First();
            Assert.Equal(LockedOpenRefusal.None, Decide(package, HiddenApps.Of([package]), link: new Uri($"https://www.{host}/watch/1")));
        }
    }

    [Fact]
    public void EveryCataloguedPackageNamesAtLeastOneLinkDomainAndNothingElse()
    {
        Assert.Equal(
            LockedOpenCatalogue.Packages.Order(StringComparer.Ordinal),
            LockedOpenCatalogue.LinkDomains.Keys.Order(StringComparer.Ordinal));
        Assert.All(LockedOpenCatalogue.LinkDomains.Values, domains => Assert.NotEmpty(domains));
    }

    [Theory]
    [InlineData("https://www.netflix.com/title/1", true)]
    [InlineData("https://netflix.com/title/1", true)]
    [InlineData("https://help.netflix.com/title/1", true)]
    [InlineData("https://NETFLIX.com/title/1", true)]
    [InlineData("https://www.example.com/title/1", false)]
    [InlineData("https://netflix.com.example.com/title/1", false)]
    [InlineData("https://notnetflix.com/title/1", false)]
    [InlineData("https://www.hulu.com/watch/1", false)]
    public void TheLinkMustBeOnTheServicesOwnDomain(string link, bool allowed)
    {
        var verdict = Decide(Netflix, link: new Uri(link));

        Assert.Equal(allowed ? LockedOpenRefusal.None : LockedOpenRefusal.LinkHostNotAllowed, verdict);
    }

    [Theory]
    [InlineData("https://www.example.com/watch/123", true)]
    [InlineData("http://www.example.com/watch/123", false)]
    [InlineData("intent://watch#Intent;end", false)]
    [InlineData("content://media/external/1", false)]
    [InlineData("https://user:pass@www.example.com/watch", false)]
    [InlineData("not a link", false)]
    [InlineData("", false)]
    public void OnlyAnHttpsLinkIsReadAsARequest(string link, bool readable)
    {
        Assert.Equal(readable, LockedOpenCodec.TryRead(Netflix, link, out var request));
        if (readable)
        {
            Assert.Equal(Netflix, request.Package);
            Assert.Equal(new Uri(link), request.Link);
        }
    }

    [Fact]
    public void AnAbsurdlyLongLinkIsRefused()
    {
        var link = "https://www.example.com/" + new string('a', LockedOpenHandoff.MaxLinkLength);

        Assert.False(LockedOpenCodec.TryRead(Netflix, link, out _));
    }

    [Fact]
    public void ANullPackageOrLinkIsUnreadable()
    {
        Assert.False(LockedOpenCodec.TryRead(null, "https://www.example.com/x", out _));
        Assert.False(LockedOpenCodec.TryRead(Netflix, null, out _));
    }
}
