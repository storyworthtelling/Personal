using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.People;
using static Pulp.Tests.Household.Records.RecordsFixtures;

namespace Pulp.Tests.Household.Records;

/// <summary>The plan record and the other adult's Google address, as household records. Invented values.</summary>
public class PlanAndAccountRecordTests
{
    private static readonly HouseholdPlan Outing =
        new("0a1b2c", Robin, "Saltmarsh at the cinema", Lantern, new DateOnly(2026, 10, 17), new TimeOnly(19, 30), "The Majestic", [Robin, Avery]);

    [Fact]
    public void APlanRoundTripsWithEveryField()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Plan, Outing, Next(), PhoneA);

        var read = HouseholdRecords.Read(document, HouseholdMaps.Plan);

        Assert.Empty(read.Unreadable);
        var back = Assert.Single(read.Items);
        Assert.Equal(Outing with { Who = [] }, back with { Who = [] });
        Assert.Equal(Outing.Who, back.Who);
    }

    [Fact]
    public void AnAllDayFreeTextPlanWithNoPlaceRoundTrips()
    {
        var plan = new HouseholdPlan("ff00", Avery, "Picnic", null, new DateOnly(2026, 11, 1), null, null, [Avery]);
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Plan, plan, Next(), PhoneA);

        var back = Assert.Single(HouseholdRecords.Read(document, HouseholdMaps.Plan).Items);

        Assert.Null(back.Title);
        Assert.Null(back.Time);
        Assert.Null(back.Place);
        Assert.Equal("Picnic", back.Text);
    }

    [Fact]
    public void ThePlanKindIsTheFrozenWord()
    {
        Assert.Equal("plan", HouseholdKinds.Plan);
        Assert.Equal("plan", HouseholdMaps.Plan.Kind);
    }

    [Fact]
    public void ADeletedPlanIsATombstoneThatOutranksTheOtherPhonesCopyAndTwoEditsMerge()
    {
        var phoneA = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Plan, Outing, Next(), PhoneA);
        var phoneB = HouseholdRecords.Put(phoneA, HouseholdMaps.Plan, Outing with { Place = "The Regent" }, Next(), PhoneB);
        phoneA = HouseholdRecords.Remove(phoneA, HouseholdMaps.Plan, Outing, Next(), PhoneA);

        Assert.Empty(HouseholdView.Of(HouseholdMerge.Merge(phoneA, phoneB)).CalendarPlans);
    }

    [Fact]
    public void AnUnreadableDateIsReportedNotDropped()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Plan, Outing, Next(), PhoneA);
        var key = HouseholdMaps.Plan.KeyOf(Outing);
        var broken = document.Find(key)!.Edit(new Dictionary<string, string> { ["date"] = "next tuesday" }, RecordSchema.Version, Next(), PhoneA);

        var read = HouseholdRecords.Read(document.With(broken), HouseholdMaps.Plan);

        Assert.Empty(read.Items);
        Assert.Single(read.Unreadable);
    }

    [Fact]
    public void AnAdultsGoogleAddressIsItsOwnFrozenKindKeyedByTheViewer()
    {
        Assert.Equal("adult-account", HouseholdKinds.AdultAccount);
        var entry = new AdultAccountEntry(Avery, "avery@example.com");
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.AdultAccount, entry, Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.AdultAccount, entry with { Email = "avery.two@example.com" }, Next(), PhoneA);

        var back = Assert.Single(HouseholdRecords.Read(document, HouseholdMaps.AdultAccount).Items);

        Assert.Equal("avery.two@example.com", back.Email);
    }

    [Fact]
    public void OnlyAnAdultsValidAddressIsOfferedForSharing()
    {
        var document = HouseholdDocument.Empty;
        document = HouseholdRecords.Put(document, HouseholdMaps.Viewer, new HouseholdViewer(Robin, false), Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.Viewer, new HouseholdViewer(Avery, false), Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.Viewer, new HouseholdViewer(Pip, true), Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.AdultAccount, new AdultAccountEntry(Avery, " Avery@Example.com "), Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.AdultAccount, new AdultAccountEntry(Pip, "pip@example.com"), Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.AdultAccount, new AdultAccountEntry(Robin, "not an address"), Next(), PhoneA);

        var view = HouseholdView.Of(document);

        Assert.Equal(["Avery@Example.com"], view.AdultGoogleAccounts);
        Assert.Equal("Avery@Example.com", view.GoogleAccountOf(Avery));
    }

    [Theory]
    [InlineData("avery@example.com", true)]
    [InlineData("  avery@example.com  ", true)]
    [InlineData("avery.k+cal@mail.example.co", true)]
    [InlineData("", false)]
    [InlineData("avery", false)]
    [InlineData("avery@", false)]
    [InlineData("@example.com", false)]
    [InlineData("avery@example", false)]
    [InlineData("a b@example.com", false)]
    [InlineData("a@@example.com", false)]
    [InlineData("avery@example..com", false)]
    public void TheShapeOfAnAddressIsChecked(string text, bool fine) =>
        Assert.Equal(fine, GoogleAddress.Check(text) is null);

    [Fact]
    public void ACleanedAddressIsTrimmedAndABlankOneIsNull()
    {
        Assert.Equal("avery@example.com", GoogleAddress.Clean("  avery@example.com "));
        Assert.Null(GoogleAddress.Clean("   "));
    }
}
