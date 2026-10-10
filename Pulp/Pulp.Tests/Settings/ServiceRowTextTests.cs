using Pulp.Domain.Availability;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Services;
using Pulp.Domain.Settings;

namespace Pulp.Tests.Settings;

/// <summary>Mockup 09's service row: name and tier (D248, D29), and one neutral "Adults only" badge (D249).</summary>
public class ServiceRowTextTests
{
    private static readonly ServiceId Streamly = new("streamly-example");
    private static readonly ServiceId Tinytales = new("tinytales-example");

    private static readonly ServiceCatalogue Catalogue = new(
    [
        new(Streamly, "Streamly", ["com.example.streamly"], "streamly", []),
        new(Tinytales, "Tinytales", ["com.example.tinytales"], "tinytales", [], kidsOnly: true),
    ]);

    [Theory]
    [InlineData(PlanTier.AdFree, VideoQuality.Uhd, "4K · no ads")]
    [InlineData(PlanTier.WithAds, VideoQuality.Hd, "HD · with ads")]
    [InlineData(PlanTier.AdFree, VideoQuality.Sd, "SD · no ads")]
    public void TheTierLineIsAsDrawn(PlanTier tier, VideoQuality picture, string expected) =>
        Assert.Equal(expected, ServiceRowText.Tier(new ServicePlanEntry(Streamly) { Tier = tier, Picture = picture }));

    [Fact]
    public void HalfAPlanSaysOnlyWhatIsKnown()
    {
        Assert.Equal("No ads", ServiceRowText.Tier(new ServicePlanEntry(Streamly) { Tier = PlanTier.AdFree }));
        Assert.Equal("4K", ServiceRowText.Tier(new ServicePlanEntry(Streamly) { Picture = VideoQuality.Uhd }));
    }

    [Fact]
    public void NoPlanSaysSoRatherThanGuessing()
    {
        Assert.Equal(ServiceRowText.NotSet, ServiceRowText.Tier(null));
        Assert.Equal(ServiceRowText.NotSet, ServiceRowText.Tier(new ServicePlanEntry(Streamly) { ShownInFlyleaf = true }));
    }

    [Theory]
    [InlineData(4, "4 of yours")]
    [InlineData(1, "1 of yours")]
    [InlineData(0, "None yet")]
    public void TheGroupHeaderCountsTheHouseholdsServices(int had, string expected) =>
        Assert.Equal(expected, ServiceRowText.Count(had));

    [Fact]
    public void AnUnattestedServiceIsAdultsOnly() =>
        Assert.True(ServiceRowText.AdultsOnly(Streamly, new AttestedServices([]), Catalogue));

    [Fact]
    public void AnAttestedServiceCarriesNoBadge() =>
        Assert.False(ServiceRowText.AdultsOnly(Streamly, new AttestedServices([Streamly]), Catalogue));

    /// <summary>A kids-only app is the child's profile (finding §2.2): no attestation, no badge.</summary>
    [Fact]
    public void AKidsOnlyAppCarriesNoBadge() =>
        Assert.False(ServiceRowText.AdultsOnly(Tinytales, new AttestedServices([]), Catalogue));

    [Fact]
    public void TheBadgeIsTheOneNeutralPhrase() => Assert.Equal("Adults only", ServiceRowText.AdultsOnlyBadge);

    [Fact]
    public void EveryTierAndPictureHasWords()
    {
        Assert.All(Enum.GetValues<PlanTier>(), tier => Assert.False(string.IsNullOrWhiteSpace(ServiceRowText.TierChoice(tier))));
        Assert.All(Enum.GetValues<VideoQuality>(), picture => Assert.False(string.IsNullOrWhiteSpace(ServiceRowText.Picture(picture))));
    }
}
