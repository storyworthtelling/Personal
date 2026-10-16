using Pulp.Domain.Calendar;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Sports;

namespace Pulp.Tests.Calendar;

/// <summary>
/// What the nightly writer is handed and what the Settings and Vault pages say about the calendar. Only the
/// owner's phone creates or writes (D145); until the owner has consented to the two calendar scopes the
/// worker must not even ask Google (an unconsented token request raises a system sign-in prompt), so a not-
/// yet-connected owner is handed the "waits" role, which writes nothing and touches no network.
/// </summary>
public class CalendarScreenSnapshotTests
{
    private static readonly ViewerId Sam = new("sam");
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    private static readonly CalendarSources Nothing = new([], [], [], []);
    private static readonly IReadOnlyDictionary<ViewerId, IReadOnlySet<LeagueId>> NoHolds =
        new Dictionary<ViewerId, IReadOnlySet<LeagueId>> { [Sam] = new HashSet<LeagueId>() };

    private static CalendarFeedSnapshot Snapshot(HouseholdFileRole role, bool connected, string? calendarId = null, string? account = "sam@example.com") =>
        CalendarScreenSnapshot.Build(Nothing, NoHolds, viewer => viewer.Value, role, connected, calendarId, account, Zone);

    [Fact]
    public void ANotYetConnectedOwnerWaitsSoNothingAsksGoogle()
    {
        Assert.Equal(HouseholdFileRole.Unknown, Snapshot(HouseholdFileRole.Owner, connected: false).Role);
    }

    [Fact]
    public void AConnectedOwnerWrites()
    {
        var snapshot = Snapshot(HouseholdFileRole.Owner, connected: true, calendarId: "cal-1");

        Assert.Equal(HouseholdFileRole.Owner, snapshot.Role);
        Assert.Equal("cal-1", snapshot.CalendarId);
        Assert.Equal("sam@example.com", snapshot.OwnerAccount);
        Assert.Equal(["sam@example.com"], snapshot.AdultAccounts);
    }

    [Fact]
    public void AConnectedOwnerSharesTheCalendarWithTheOtherAdultOnceAndOnlyWhenItWrites()
    {
        var shared = CalendarScreenSnapshot.Build(
            Nothing, NoHolds, viewer => viewer.Value, HouseholdFileRole.Owner, true, "cal-1", "sam@example.com", Zone,
            ["avery@example.com", "SAM@example.com", "avery@example.com"]);
        var notConnected = CalendarScreenSnapshot.Build(
            Nothing, NoHolds, viewer => viewer.Value, HouseholdFileRole.Owner, false, "cal-1", "sam@example.com", Zone,
            ["avery@example.com"]);
        var herPhone = CalendarScreenSnapshot.Build(
            Nothing, NoHolds, viewer => viewer.Value, HouseholdFileRole.SharedWith, false, "cal-1", "avery@example.com", Zone,
            ["avery@example.com"]);

        Assert.Equal(["sam@example.com", "avery@example.com"], shared.AdultAccounts);
        Assert.Empty(notConnected.AdultAccounts);
        Assert.Empty(herPhone.AdultAccounts);
    }

    [Fact]
    public void TheOtherAdultsPhoneKeepsItsRoleAndNeedsNoConsent()
    {
        var snapshot = Snapshot(HouseholdFileRole.SharedWith, connected: false, calendarId: "cal-1");

        Assert.Equal(HouseholdFileRole.SharedWith, snapshot.Role);
        Assert.Equal("cal-1", snapshot.CalendarId);
        Assert.Empty(snapshot.AdultAccounts);
    }

    [Fact]
    public void AnOwnerWithNoSignedInAccountWaits()
    {
        Assert.Equal(HouseholdFileRole.Unknown, Snapshot(HouseholdFileRole.Owner, connected: true, account: null).Role);
    }

    [Fact]
    public void TheSnapshotCarriesTheSourcesHoldsAndZoneItWasGiven()
    {
        var snapshot = Snapshot(HouseholdFileRole.Owner, connected: true);

        Assert.Same(Nothing, snapshot.Sources);
        Assert.Same(NoHolds, snapshot.HeldByAdult);
        Assert.Equal(Zone, snapshot.Zone);
        Assert.Equal("sam", snapshot.NameOf(Sam));
    }

    [Theory]
    [InlineData(HouseholdFileRole.Owner, false, "Calendar not connected yet")]
    [InlineData(HouseholdFileRole.Owner, true, "Calendar connected. Pulp writes the household calendar each night.")]
    [InlineData(HouseholdFileRole.SharedWith, false, "The household calendar is written from the owner's phone.")]
    [InlineData(HouseholdFileRole.Unknown, false, "The calendar waits until this phone knows whose it is.")]
    public void TheStatusSentenceSaysWhereTheCalendarStands(HouseholdFileRole role, bool connected, string expected)
    {
        Assert.Equal(expected, CalendarScreenConnection.Sentence(role, connected));
    }

    [Fact]
    public void OnlyTheOwnerIsOfferedTheConsentButton()
    {
        Assert.True(CalendarScreenConnection.OffersConnect(HouseholdFileRole.Owner, connected: false));
        Assert.False(CalendarScreenConnection.OffersConnect(HouseholdFileRole.Owner, connected: true));
        Assert.False(CalendarScreenConnection.OffersConnect(HouseholdFileRole.SharedWith, connected: false));
        Assert.False(CalendarScreenConnection.OffersConnect(HouseholdFileRole.Unknown, connected: false));
    }
}
