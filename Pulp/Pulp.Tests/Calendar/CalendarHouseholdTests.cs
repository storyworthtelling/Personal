using Pulp.Domain.Calendar;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;

namespace Pulp.Tests.Calendar;

/// <summary>D145: created in the owner's account only, shared to the other adult, found by id.</summary>
public class CalendarHouseholdTests
{
    [Theory]
    [InlineData(HouseholdFileRole.Owner, null, CalendarStand.CreateAndWrite)]
    [InlineData(HouseholdFileRole.Owner, "cal-1", CalendarStand.Write)]
    [InlineData(HouseholdFileRole.SharedWith, "cal-1", CalendarStand.Find)]
    [InlineData(HouseholdFileRole.SharedWith, null, CalendarStand.Wait)]
    [InlineData(HouseholdFileRole.Unknown, null, CalendarStand.Wait)]
    [InlineData(HouseholdFileRole.Unknown, "cal-1", CalendarStand.Wait)]
    public void OnlyTheOwnerCreatesOrWrites(HouseholdFileRole role, string? id, CalendarStand expected)
    {
        Assert.Equal(expected, PulpCalendarAccess.Decide(role, id));
    }

    [Fact]
    public void SharingSkipsTheOwnerAndWhoeverAlreadyHasIt()
    {
        var missing = CalendarSharing.Missing(
            "owner@example.com", ["Owner@example.com", "partner@example.com", "guest@example.com", " "], ["GUEST@example.com"]);
        Assert.Equal(["partner@example.com"], missing);
    }

    [Fact]
    public void TheOtherAdultGetsReadOnly()
    {
        Assert.Equal("reader", CalendarSharing.Role);
    }

    [Fact]
    public async Task TheOwnerCreatesSavesTheIdThenShares()
    {
        var calendar = new FakePulpCalendar();
        var order = new List<string>();
        calendar.OnCreate = () => order.Add("create");
        calendar.OnShare = () => order.Add("share");

        var id = await CalendarHousehold.EnsureAsync(
            calendar, HouseholdFileRole.Owner, null, "owner@example.com", ["owner@example.com", "partner@example.com"],
            _ => { order.Add("save"); return Task.CompletedTask; }, TimeZoneInfo.Utc, CancellationToken.None);

        Assert.Equal("cal-new", id);
        Assert.Equal(["create", "save", "share"], order);
        Assert.Equal(["partner@example.com"], calendar.Shared);
    }

    /// <summary>The bug this holds: the other adult's install making a second calendar nobody merges with.</summary>
    [Theory]
    [InlineData(HouseholdFileRole.SharedWith)]
    [InlineData(HouseholdFileRole.Unknown)]
    public async Task NoOneButTheOwnerEverCreates(HouseholdFileRole role)
    {
        var calendar = new FakePulpCalendar();
        var id = await CalendarHousehold.EnsureAsync(
            calendar, role, null, "partner@example.com", ["partner@example.com"], _ => Task.CompletedTask, TimeZoneInfo.Utc, CancellationToken.None);

        Assert.Null(id);
        Assert.Equal(0, calendar.Creates);
        Assert.Empty(calendar.Shared);
    }

    [Fact]
    public async Task AnExistingIdIsReusedNotRecreated()
    {
        var calendar = new FakePulpCalendar();
        var id = await CalendarHousehold.EnsureAsync(
            calendar, HouseholdFileRole.Owner, "cal-1", "owner@example.com", ["owner@example.com", "partner@example.com"],
            _ => Task.CompletedTask, TimeZoneInfo.Utc, CancellationToken.None);

        Assert.Equal("cal-1", id);
        Assert.Equal(0, calendar.Creates);
        Assert.Equal(["partner@example.com"], calendar.Shared);
    }

    [Fact]
    public async Task TheOtherAdultFindsItByIdAndWritesNothing()
    {
        var calendar = new FakePulpCalendar();
        var id = await CalendarHousehold.EnsureAsync(
            calendar, HouseholdFileRole.SharedWith, "cal-1", "partner@example.com", ["owner@example.com", "partner@example.com"],
            _ => Task.CompletedTask, TimeZoneInfo.Utc, CancellationToken.None);

        Assert.Equal("cal-1", id);
        Assert.Empty(calendar.Shared);
    }

    // ---- the household record ----

    [Fact]
    public void TheCalendarIdRoundTripsThroughTheHouseholdFile()
    {
        var entry = new PulpCalendarEntry("cal-1");
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.PulpCalendar, entry, new HlcTimestamp(1, 0), "phone-a");

        Assert.Equal("cal-1", HouseholdView.Of(document).PulpCalendarId);
        Assert.Null(HouseholdView.Of(HouseholdDocument.Empty).PulpCalendarId);
    }
}
