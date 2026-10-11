using Pulp.Domain.Availability;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.People;
using static Pulp.Tests.Household.Records.RecordsFixtures;

namespace Pulp.Tests.Settings;

/// <summary>
/// The two records Settings adds (D29, D86, D8; the Household page's rename): a service's plan, and the
/// name a viewer goes by. Both round-trip, both leave a missing value missing, and the view reads them
/// into what the screens need — the best home's plans and a viewer's name.
/// </summary>
public class SettingsRecordsTests
{
    [Fact]
    public void APlanRoundTripsEveryField()
    {
        var plan = new ServicePlanEntry(Streamly) { Tier = PlanTier.WithAds, Picture = VideoQuality.Hd, ShownInFlyleaf = true };
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.ServicePlan, plan, Next(), PhoneA);

        var read = HouseholdRecords.Read(document, HouseholdMaps.ServicePlan);

        Assert.Empty(read.Unreadable);
        Assert.Equal(plan, Assert.Single(read.Items));
    }

    /// <summary>An unset tier is not set — read back as "ad-free" it would stop the page asking.</summary>
    [Fact]
    public void AnUnsetTierAndPictureStayUnset()
    {
        var plan = new ServicePlanEntry(Streamly);
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.ServicePlan, plan, Next(), PhoneA);

        var back = Assert.Single(HouseholdRecords.Read(document, HouseholdMaps.ServicePlan).Items);

        Assert.Null(back.Tier);
        Assert.Null(back.Picture);
        Assert.False(back.ShownInFlyleaf);
    }

    [Fact]
    public void TheTierIsStoredAsItsOwnWordNotTheEnumName()
    {
        var fields = HouseholdMaps.ServicePlan.FieldsOf(new ServicePlanEntry(Streamly) { Tier = PlanTier.AdFree, Picture = VideoQuality.Uhd });

        Assert.Equal("ad-free", fields["tier"]);
        Assert.Equal("uhd", fields["picture"]);
        Assert.Equal("false", fields["shownInFlyleaf"]);
    }

    [Fact]
    public void ATierWordThisBuildDoesNotKnowIsReportedNotGuessed()
    {
        var key = HouseholdMaps.ServicePlan.KeyOf(new ServicePlanEntry(Streamly));
        var record = HouseholdRecord.Write(
            key, RecordSchema.Version,
            new Dictionary<string, string> { ["service"] = Streamly.Value, ["tier"] = "premium-plus", ["picture"] = "", ["shownInFlyleaf"] = "false" },
            Next(), PhoneA);

        var read = HouseholdRecords.Read(HouseholdDocument.Empty.With(record), HouseholdMaps.ServicePlan);

        Assert.Empty(read.Items);
        Assert.Contains("tier", Assert.Single(read.Unreadable).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANameRoundTrips()
    {
        var name = new ViewerNameEntry(Robin, "Robin Example");
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.ViewerName, name, Next(), PhoneA);

        Assert.Equal(name, Assert.Single(HouseholdRecords.Read(document, HouseholdMaps.ViewerName).Items));
    }

    [Fact]
    public void AViewerIsCalledByTheirNameRecordElseTheirId()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.ViewerName, new ViewerNameEntry(Robin, "Robbie"), Next(), PhoneA);

        var view = HouseholdView.Of(document);

        Assert.Equal("Robbie", view.NameOf(Robin));
        Assert.Equal(Avery.Value, view.NameOf(Avery));
    }

    /// <summary>
    /// D29: the best home reads each service's recorded tier. Before the record existed every screen
    /// took every plan as ad-free; a with-ads plan must now say so, and an unset one changes nothing.
    /// </summary>
    [Fact]
    public void TheBestHomeReadsTheRecordedTier()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.HouseholdService, new HouseholdServiceEntry(Streamly), Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.HouseholdService, new HouseholdServiceEntry(Flicker), Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.ServicePlan, new ServicePlanEntry(Streamly) { Tier = PlanTier.WithAds }, Next(), PhoneA);

        var plans = HouseholdView.Of(document).ServicePlans;

        Assert.Equal(2, plans.Count);
        Assert.Contains(new ServicePlan(Streamly, AdFree: false), plans);
        Assert.Contains(new ServicePlan(Flicker, AdFree: true), plans);
    }

    /// <summary>A plan kept for a service the household no longer has must not put it back among the homes.</summary>
    [Fact]
    public void APlanForAServiceNotHadIsNotAHome()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.ServicePlan, new ServicePlanEntry(Streamly) { Tier = PlanTier.AdFree }, Next(), PhoneA);

        var view = HouseholdView.Of(document);

        Assert.Empty(view.ServicePlans);
        Assert.Equal(PlanTier.AdFree, view.PlanOf(Streamly)?.Tier);
        Assert.Null(view.PlanOf(Flicker));
    }

    [Fact]
    public void AWithAdsPlanLosesATieOnTheBestHome()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.HouseholdService, new HouseholdServiceEntry(Streamly), Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.HouseholdService, new HouseholdServiceEntry(Flicker), Next(), PhoneA);
        // Streamly sorts first by id, so without the tier it would win the tie.
        document = HouseholdRecords.Put(document, HouseholdMaps.ServicePlan, new ServicePlanEntry(Streamly) { Tier = PlanTier.WithAds }, Next(), PhoneA);
        var view = HouseholdView.Of(document);
        var offers = new[] { new StreamOffer(Streamly, VideoQuality.Hd), new StreamOffer(Flicker, VideoQuality.Hd) };

        var choice = BestHome.Choose(Lantern, offers, view.ServicePlans, view.HomeOverrides);

        Assert.Equal(Flicker, choice.Best?.Service);
        Assert.True(Assert.Single(choice.Others).WithAds);
    }
}
