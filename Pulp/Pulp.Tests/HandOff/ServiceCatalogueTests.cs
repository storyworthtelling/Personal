using Pulp.Domain.Core;
using Pulp.Domain.Services;

namespace Pulp.Tests.HandOff;

/// <summary>
/// The service table: Pulp's id ↔ Android package(s) ↔ the availability source's id ↔ the merchant
/// names Index recognises a subscription charge by. Public catalogue data only — the table says
/// nothing about which services any household has.
/// </summary>
public class ServiceCatalogueTests
{
    private static StreamingService Invented(string id, string package, string source, params string[] merchants) =>
        new(new ServiceId(id), id, [package], source, merchants);

    [Fact]
    public void ThePublicCatalogueIsNotEmptyAndEveryEntryCanBeHandedTo()
    {
        var all = ServiceCatalogue.Public.All;

        Assert.NotEmpty(all);
        Assert.All(all, service =>
        {
            Assert.NotEmpty(service.Packages);
            Assert.All(service.Packages, package => Assert.False(string.IsNullOrWhiteSpace(package)));
            Assert.False(string.IsNullOrWhiteSpace(service.SourceId));
            Assert.False(string.IsNullOrWhiteSpace(service.Name));
            Assert.NotEmpty(service.MerchantNames);
        });
    }

    [Fact]
    public void ThePublicCatalogueIsOneToOneOnEveryKey()
    {
        var all = ServiceCatalogue.Public.All;

        AssertUnique(all.Select(service => service.Id.Value));
        AssertUnique(all.SelectMany(service => service.Packages));
        AssertUnique(all.Select(service => service.SourceId));
        AssertUnique(all.SelectMany(service => service.MerchantNames));
    }

    [Fact]
    public void EveryPublicEntryIsFoundBackByEachOfItsKeys()
    {
        var catalogue = ServiceCatalogue.Public;

        Assert.All(catalogue.All, service =>
        {
            Assert.Same(service, catalogue.ById(service.Id));
            Assert.Same(service, catalogue.BySourceId(service.SourceId));
            Assert.All(service.Packages, package => Assert.Same(service, catalogue.ByPackage(package)));
            Assert.All(service.MerchantNames, merchant => Assert.Same(service, catalogue.ByMerchant(merchant)));
        });
    }

    [Fact]
    public void LookupsIgnoreCaseAndSurroundingSpace()
    {
        var flix = Invented("example-flix", "com.example.flix", "exampleflix", "EXAMPLEFLIX.COM", "Exampleflix");
        var catalogue = new ServiceCatalogue([flix]);

        Assert.Same(flix, catalogue.ByMerchant("  exampleflix.com "));
        Assert.Same(flix, catalogue.ByMerchant("EXAMPLEFLIX"));
        Assert.Same(flix, catalogue.BySourceId("ExampleFlix"));
        Assert.Same(flix, catalogue.ByPackage("COM.EXAMPLE.FLIX"));
        Assert.Same(flix, catalogue.ById(new ServiceId("EXAMPLE-FLIX")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SOMEWHERE ELSE")]
    public void AnUnknownKeyFindsNothingRatherThanTheNearestService(string? key)
    {
        var catalogue = new ServiceCatalogue([Invented("example-flix", "com.example.flix", "exampleflix", "Exampleflix")]);

        Assert.Null(catalogue.ByMerchant(key));
        Assert.Null(catalogue.ByPackage(key));
        Assert.Null(catalogue.BySourceId(key));
    }

    [Fact]
    public void AMerchantNameIsMatchedWholeNeverAsAFragment()
    {
        // "Exampleflix Store" is not the subscription; a contains-match would count it as one.
        var catalogue = new ServiceCatalogue([Invented("example-flix", "com.example.flix", "exampleflix", "Exampleflix")]);

        Assert.Null(catalogue.ByMerchant("Exampleflix Store"));
    }

    [Fact]
    public void ACatalogueRefusesTwoServicesClaimingOnePackage()
    {
        var error = Assert.Throws<ArgumentException>(() => new ServiceCatalogue(
        [
            Invented("example-flix", "com.example.flix", "exampleflix", "Exampleflix"),
            Invented("sample-stream", "COM.EXAMPLE.FLIX", "samplestream", "Samplestream"),
        ]));

        Assert.Contains("com.example.flix", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACatalogueRefusesTwoServicesClaimingOneMerchantName() =>
        Assert.Throws<ArgumentException>(() => new ServiceCatalogue(
        [
            Invented("example-flix", "com.example.flix", "exampleflix", "Example Media"),
            Invented("sample-stream", "com.example.sample", "samplestream", "EXAMPLE MEDIA"),
        ]));

    [Fact]
    public void ACatalogueRefusesTwoServicesClaimingOneSourceId() =>
        Assert.Throws<ArgumentException>(() => new ServiceCatalogue(
        [
            Invented("example-flix", "com.example.flix", "exampleflix", "Exampleflix"),
            Invented("sample-stream", "com.example.sample", "EXAMPLEFLIX", "Samplestream"),
        ]));

    [Fact]
    public void ACatalogueRefusesTheSameServiceIdTwice() =>
        Assert.Throws<ArgumentException>(() => new ServiceCatalogue(
        [
            Invented("example-flix", "com.example.flix", "exampleflix", "Exampleflix"),
            Invented("Example-Flix", "com.example.sample", "samplestream", "Samplestream"),
        ]));

    [Fact]
    public void TheFirstPackageIsTheOneHandedTo()
    {
        var service = new StreamingService(
            new ServiceId("example-flix"), "Exampleflix", ["com.example.flix", "com.example.flix.legacy"], "exampleflix", ["Exampleflix"]);

        Assert.Equal("com.example.flix", service.PrimaryPackage);
    }

    [Fact]
    public void AServiceIsNotAKidsOnlyAppUnlessSaidSo() =>
        Assert.False(Invented("example-flix", "com.example.flix", "exampleflix", "Exampleflix").KidsOnly);

    private static void AssertUnique(IEnumerable<string> keys)
    {
        var list = keys.ToList();
        Assert.NotEmpty(list);
        var duplicates = list.GroupBy(key => key.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        Assert.True(duplicates.Count == 0, "Claimed by two services: " + string.Join(", ", duplicates));
    }
}
