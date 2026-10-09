using System.Reflection;
using Pulp.Domain.Core;
using Pulp.Domain.Services;

namespace Pulp.Tests.HandOff;

/// <summary>
/// G8 — a hand-off is never built without an explicit package (finding §2.4). On the locked phone
/// Flyleaf pins its restricted browser as the default web handler, so an https link sent without
/// <c>setPackage</c> falls to it and shows the Blocked page instead of the service. The spec is built
/// only through <see cref="Pulp.Domain.Services.HandOff.For"/>, and only from a catalogue service,
/// which cannot exist without a package.
/// </summary>
public class HandOffTests
{
    private static readonly StreamingService Flix = new(
        new ServiceId("example-flix"), "Exampleflix", ["com.example.flix", "com.example.flix.legacy"], "exampleflix", ["Exampleflix"]);

    private static HandOffLink Link(string link, string? videoLink = null, string service = "example-flix") =>
        new(new TitleId("title-lantern-cove"), new ServiceId(service), new Uri(link), videoLink is null ? null : new Uri(videoLink));

    [Fact]
    public void AHandOffIsAViewIntentAimedAtTheServicesOwnPackage()
    {
        var intent = Pulp.Domain.Services.HandOff.For(Link("https://watch.example.com/title/100"), Flix);

        Assert.Equal("android.intent.action.VIEW", intent.Action);
        Assert.Equal("com.example.flix", intent.Package);
        Assert.Equal(new Uri("https://watch.example.com/title/100"), intent.Data);
    }

    [Fact]
    public void TheVideoLinkIsPreferredWhenTheSourceHasOne()
    {
        var intent = Pulp.Domain.Services.HandOff.For(
            Link("https://watch.example.com/title/100", "https://watch.example.com/play/100?episode=4"), Flix);

        Assert.Equal(new Uri("https://watch.example.com/play/100?episode=4"), intent.Data);
    }

    [Fact]
    public void EveryPublicServiceYieldsAnIntentCarryingItsPackage()
    {
        var all = ServiceCatalogue.Public.All;
        Assert.NotEmpty(all);

        Assert.All(all, service =>
        {
            var intent = Pulp.Domain.Services.HandOff.For(Link("https://watch.example.com/title/100", service: service.Id.Value), service);
            Assert.False(string.IsNullOrWhiteSpace(intent.Package));
            Assert.Equal(service.PrimaryPackage, intent.Package);
        });
    }

    [Fact]
    public void TheIntentSpecCannotBeConstructedOutsideHandOff()
    {
        var constructors = typeof(HandOffIntent).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(constructors);
        Assert.True(typeof(HandOffIntent).IsSealed);
    }

    [Fact]
    public void TheIntentSpecsPackageCannotBeChangedAfterItIsBuilt()
    {
        var package = typeof(HandOffIntent).GetProperty(nameof(HandOffIntent.Package));

        Assert.NotNull(package);
        Assert.Null(package.SetMethod);
        Assert.Equal(typeof(string), package.PropertyType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AServiceCannotExistWithoutARealPackage(int shape)
    {
        string[] packages = shape switch
        {
            0 => [],
            1 => [""],
            _ => ["com.example.flix", " "],
        };

        Assert.Throws<ArgumentException>(() =>
            new StreamingService(new ServiceId("example-flix"), "Exampleflix", packages, "exampleflix", ["Exampleflix"]));
    }

    [Fact]
    public void ALinkForOneServiceIsNeverSentToAnother() =>
        Assert.Throws<ArgumentException>(() =>
            Pulp.Domain.Services.HandOff.For(Link("https://watch.example.com/title/100", service: "sample-stream"), Flix));

    [Theory]
    [InlineData("http://watch.example.com/title/100")]
    [InlineData("intent://watch.example.com/title/100")]
    [InlineData("file:///sdcard/title.html")]
    public void OnlyAnHttpsLinkIsHandedOff(string link) =>
        Assert.Throws<ArgumentException>(() => Pulp.Domain.Services.HandOff.For(Link(link), Flix));

    [Fact]
    public void AnInsecureVideoLinkIsRefusedRatherThanQuietlySkipped() =>
        Assert.Throws<ArgumentException>(() => Pulp.Domain.Services.HandOff.For(
            Link("https://watch.example.com/title/100", "http://watch.example.com/play/100"), Flix));
}
