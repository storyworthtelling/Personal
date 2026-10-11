using System.Text.Json;
using Pulp.Domain.Core;
using Pulp.Domain.Hidden;
using Pulp.Domain.Services;

namespace Pulp.Tests.Hidden;

/// <summary>
/// D92: Flyleaf hides exactly the services ticked in Pulp, by package id, and nothing else. The set
/// is built from the public catalogue and the household's ticked ids.
/// </summary>
public class HiddenAppsHandOffTests
{
    private static readonly ServiceCatalogue Catalogue = ServiceCatalogue.Public;

    private static ServiceId Id(string value) => new(value);

    [Fact]
    public void TheSetIsEveryPackageOfEveryTickedServiceAndNoOther()
    {
        var packages = HiddenAppsHandOff.PackagesFor(Catalogue, [Id("netflix"), Id("hbo-max")]);

        Assert.Equal(
            ["com.hbo.hbonow", "com.netflix.mediaclient", "com.wbd.stream"],
            packages);
        Assert.DoesNotContain("com.hulu.plus", packages);
    }

    [Fact]
    public void AServiceNotTickedIsNotHidden()
    {
        var packages = HiddenAppsHandOff.PackagesFor(Catalogue, [Id("hulu")]);

        Assert.Equal(["com.hulu.plus"], packages);
    }

    [Fact]
    public void NothingTickedIsAnEmptySetThatStillCrossesSoUntickingUnhides()
    {
        var packages = HiddenAppsHandOff.PackagesFor(Catalogue, []);

        Assert.Empty(packages);
        Assert.Equal("[]", HiddenAppsHandOff.Extras(packages)[HiddenAppsHandOff.PackagesExtra]);
    }

    [Fact]
    public void AnIdTheCatalogueDoesNotKnowIsIgnoredNotInvented()
    {
        var packages = HiddenAppsHandOff.PackagesFor(Catalogue, [Id("no-such-service"), Id("peacock")]);

        Assert.Equal(["com.peacocktv.peacockandroid"], packages);
    }

    [Fact]
    public void ATickedServiceListedTwiceHidesItsPackagesOnce()
    {
        var packages = HiddenAppsHandOff.PackagesFor(Catalogue, [Id("netflix"), Id("netflix")]);

        Assert.Single(packages);
    }

    [Fact]
    public void ThePayloadIsOneExactTextExtraHoldingAJsonArray()
    {
        var extras = HiddenAppsHandOff.Extras(["com.b.two", "com.a.one"]);

        var only = Assert.Single(extras);
        Assert.Equal(HiddenAppsHandOff.PackagesExtra, only.Key);
        Assert.Equal(["com.b.two", "com.a.one"], JsonSerializer.Deserialize<string[]>(only.Value));
    }

    [Fact]
    public void TheCatalogueHasSomethingToHideSoTheGuardsAreNotVacuous()
    {
        var everything = HiddenAppsHandOff.PackagesFor(Catalogue, Catalogue.All.Select(service => service.Id));

        Assert.True(everything.Count >= Catalogue.All.Count);
    }
}
