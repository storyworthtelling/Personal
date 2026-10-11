using Pulp.Domain.LockedOpen;

namespace Pulp.Tests.LockedOpen;

/// <summary>
/// D298: the locked hand-off is chosen only where it can land. It needs Flyleaf on this phone and the
/// service's package in the set Pulp told Flyleaf to hide; anywhere else (her unrestricted phone has no
/// Flyleaf at all) the hand-off is today's plain one, so a broadcast nobody receives can never be the
/// reason a title does not open.
/// </summary>
public class LockedOpenChoiceTests
{
    private const string Netflix = "com.netflix.mediaclient";

    private static LockedOpenFacts Facts(bool installed, params string[] hidden) => new(installed, new HashSet<string>(hidden));

    [Fact]
    public void FlyleafPresentAndTheServiceHiddenChoosesLocked()
    {
        Assert.Equal(HandOffRoute.Locked, LockedOpenChoice.Decide(Facts(true, Netflix), Netflix));
    }

    [Fact]
    public void NoFlyleafChoosesPlain()
    {
        Assert.Equal(HandOffRoute.Plain, LockedOpenChoice.Decide(Facts(false, Netflix), Netflix));
    }

    [Fact]
    public void AServiceFlyleafWasNeverToldAboutChoosesPlain()
    {
        Assert.Equal(HandOffRoute.Plain, LockedOpenChoice.Decide(Facts(true, "com.hulu.plus"), Netflix));
        Assert.Equal(HandOffRoute.Plain, LockedOpenChoice.Decide(Facts(true), Netflix));
    }

    [Fact]
    public void NoFactsAtAllChoosesPlain()
    {
        Assert.Equal(HandOffRoute.Plain, LockedOpenChoice.Decide(null, Netflix));
    }

    [Fact]
    public void ExtrasCarryExactlyThePackageAndTheLink()
    {
        var extras = LockedOpenHandOff.Extras(Netflix, new Uri("https://www.example.com/title/harbor-lights"));

        Assert.Equal(2, extras.Count);
        Assert.Equal(Netflix, extras[LockedOpenHandOff.PackageExtra]);
        Assert.Equal("https://www.example.com/title/harbor-lights", extras[LockedOpenHandOff.LinkExtra]);
    }

    [Fact]
    public void ARequestForAnythingButHttpsIsNotBuilt()
    {
        Assert.Throws<ArgumentException>(() => LockedOpenHandOff.Extras(Netflix, new Uri("http://www.example.com/x")));
        Assert.Throws<ArgumentException>(() => LockedOpenHandOff.Extras("", new Uri("https://www.example.com/x")));
    }

    [Fact]
    public void FactsAreBuiltFromTheTickedServicesAllPackages()
    {
        var facts = LockedOpenFacts.For(
            true,
            Pulp.Domain.Services.ServiceCatalogue.Public,
            [new Pulp.Domain.Core.ServiceId("hbo-max")]);

        Assert.Contains("com.wbd.stream", facts.HiddenPackages);
        Assert.Contains("com.hbo.hbonow", facts.HiddenPackages);
        Assert.DoesNotContain(Netflix, facts.HiddenPackages);
    }
}
