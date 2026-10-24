using Constitution.Domain.Calendar;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.Calendar;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.Setups;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The calendar and learning side of the weekly run's seams, as far as it can be decided without a phone: the files it
/// keeps, the sign-in that is missing, the setup that is not there, the settings that are assumed, and the two sources
/// that honestly say they are unavailable.
/// </summary>
public sealed class CalendarSideSeamsTests : IDisposable
{
    private static readonly DateTimeOffset From = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(7);

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-calendar-side-");

    public void Dispose() => folder.Delete(recursive: true);

    private string PathOf(string name) => Path.Combine(folder.FullName, "nested", name);

    [Fact]
    public void NoWrittenKeysFileIsAnEmptyRecord()
    {
        var read = new FileWrittenKeys(PathOf("written.json")).Load();

        Assert.True(read.IsReadable);
        Assert.Empty(read.Keys.Keys);
    }

    [Fact]
    public void WrittenKeysSurviveTheFile()
    {
        var store = new FileWrittenKeys(PathOf("written.json"));
        var key = PlacementKey.ForWorkout("Sam", SessionKind.Strength, 105703, 0);
        store.Save(WrittenKeys.Empty.With([key]));

        var back = new FileWrittenKeys(PathOf("written.json")).Load();

        Assert.True(back.IsReadable);
        Assert.Single(back.Keys.Keys);
    }

    [Fact]
    public void AGarbledWrittenKeysFileIsUnreadableAndKept()
    {
        var path = PathOf("written.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        var read = new FileWrittenKeys(path).Load();

        Assert.False(read.IsReadable);
        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    [Fact]
    public async Task UnavailableConditionsSaysWhyAndHasNoHours()
    {
        var read = await new UnavailableConditions().ReadAsync(From, To, CancellationToken.None);

        Assert.False(read.IsAvailable);
        Assert.Equal(UnavailableConditions.Reason, read.UnavailableBecause);
        Assert.Empty(read.Hours);
    }

    [Fact]
    public async Task UnavailableGymsNeverFindOne()
    {
        var result = await new UnavailableHotelGyms().FindAsync(new HotelStay("Invented Inn"), CancellationToken.None);

        Assert.IsType<HotelGymResult.Unavailable>(result);
    }

    [Fact]
    public async Task NotSignedInIsATypedReasonNotACrash()
    {
        var service = GoogleCalendarServices.Create(() => false, _ => Task.FromResult("token"), "Constitution tests");

        var failure = await Assert.ThrowsAsync<CalendarAccessException>(() => service(CancellationToken.None));

        Assert.Equal(GoogleCalendarServices.NotSignedIn, failure.Message);
    }

    [Fact]
    public async Task ARefusedTokenIsATypedReasonWithoutIt()
    {
        var service = GoogleCalendarServices.Create(() => true, _ => throw new InvalidOperationException("secret-detail"), "Constitution tests");

        var failure = await Assert.ThrowsAsync<CalendarAccessException>(() => service(CancellationToken.None));

        Assert.Equal(GoogleCalendarServices.SignInRefused, failure.Message);
        Assert.DoesNotContain("secret-detail", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABlankTokenIsRefusedToo()
    {
        var service = GoogleCalendarServices.Create(() => true, _ => Task.FromResult("  "), "Constitution tests");

        var failure = await Assert.ThrowsAsync<CalendarAccessException>(() => service(CancellationToken.None));

        Assert.Equal(GoogleCalendarServices.SignInRefused, failure.Message);
    }

    [Fact]
    public async Task ATokenBuildsAService()
    {
        var service = GoogleCalendarServices.Create(() => true, _ => Task.FromResult("a-token"), "Constitution tests");

        Assert.NotNull(await service(CancellationToken.None));
    }

    [Fact]
    public async Task NoSignInReadsEveryCalendarUnreadable()
    {
        var service = GoogleCalendarServices.Create(() => false, _ => Task.FromResult("token"), "Constitution tests");
        var source = new GoogleHouseholdCalendars(service, () => new HouseholdSetupRead.Configured(HouseholdData.Setup()));

        var snapshot = await source.ReadAsync(From, To, CancellationToken.None);

        Assert.Equal(GoogleCalendarServices.NotSignedIn, snapshot.OwnCalendar("Sam", From, To).UnreadableBecause);
        Assert.Equal(GoogleCalendarServices.NotSignedIn, snapshot.OwnCalendar("Lee", From, To).UnreadableBecause);
        Assert.Equal(GoogleCalendarServices.NotSignedIn, snapshot.FamilyCalendar(From, To).UnreadableBecause);
        Assert.Equal(GoogleCalendarServices.NotSignedIn, snapshot.ChildrensCalendars(From, To).UnreadableBecause);
    }

    [Fact]
    public void NoSetupFileIsNotSetWithAReason()
    {
        var read = PlanningSetupFile.Read(PathOf("planning-setup.json"));

        Assert.Equal(PlanningSetupCodec.NothingSavedReason, Assert.IsType<HouseholdSetupRead.NotSet>(read).Reason);
    }

    [Fact]
    public void ASetupFileIsRead()
    {
        var path = PathOf("planning-setup.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, HouseholdData.File);

        var read = PlanningSetupFile.Read(path);

        Assert.Equal("sam@example.com", Assert.IsType<HouseholdSetupRead.Configured>(read).Setup.CalendarOf("Sam"));
    }

    [Fact]
    public void AFileThatCannotBeOpenedIsNotSetAndQuotesNothing()
    {
        // A folder where the file should be: reading it fails with an IO error, not a missing file.
        var path = PathOf("planning-setup.json");
        Directory.CreateDirectory(path);

        var read = PlanningSetupFile.Read(path);

        Assert.Equal(PlanningSetupFile.UnreadableReason, Assert.IsType<HouseholdSetupRead.NotSet>(read).Reason);
    }

    [Fact]
    public void ReadyNeedsASignInAndASetup()
    {
        var setup = new HouseholdSetupRead.Configured(HouseholdData.Setup());

        Assert.True(CalendarSideStanding.Decide(true, setup, null, TimeZoneInfo.Utc).IsReady);
        Assert.Equal(CalendarSideStanding.SignInReason, CalendarSideStanding.Decide(false, setup, null, TimeZoneInfo.Utc).NotReadyBecause);
    }

    [Fact]
    public void NoSetupIsNotReadyForThatReason()
    {
        var standing = CalendarSideStanding.Decide(true, new HouseholdSetupRead.NotSet(PlanningSetupCodec.NothingSavedReason), null, TimeZoneInfo.Utc);

        Assert.Equal(PlanningSetupCodec.NothingSavedReason, standing.NotReadyBecause);
        Assert.Null(standing.Owner);
        Assert.Empty(standing.Assumed);
    }

    [Fact]
    public void TheOwnerIsTheAdultTheTabletDeliversFor()
    {
        var standing = CalendarSideStanding.Decide(true, new HouseholdSetupRead.Configured(HouseholdData.Setup()), null, TimeZoneInfo.Utc);

        Assert.Equal("Sam", standing.Owner);
    }

    [Fact]
    public void NobodyDeliveredForLeavesNoOwner()
    {
        var setup = new PlanningSetup([new HouseholdAdult("Sam", "sam@example.com")]) { NoFamilyCalendar = true, NoChildren = true };

        var standing = CalendarSideStanding.Decide(true, new HouseholdSetupRead.Configured(setup), null, TimeZoneInfo.Utc);

        Assert.Null(standing.Owner);
    }

    [Fact]
    public void WhatIsNotGivenIsReportedAsAssumed()
    {
        var setup = new PlanningSetup([new HouseholdAdult("Sam", "sam@example.com", DeliversHere: true)]) { NoFamilyCalendar = true, NoChildren = true };

        var standing = CalendarSideStanding.Decide(true, new HouseholdSetupRead.Configured(setup), null, TimeZoneInfo.Utc);

        Assert.Contains(AssumedSetting.DinnerTime, standing.Assumed);
        Assert.Contains(AssumedSetting.WorkHours, standing.Assumed);
        Assert.Contains(AssumedSetting.TimeZone, standing.Assumed);
        Assert.Contains("assumed", standing.Line, StringComparison.Ordinal);
        Assert.Contains("Dinner time", standing.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void AFullSetupAssumesOnlyTheLunchGap()
    {
        var standing = CalendarSideStanding.Decide(true, new HouseholdSetupRead.Configured(HouseholdData.Setup()), null, TimeZoneInfo.Utc);

        Assert.DoesNotContain(AssumedSetting.DinnerTime, standing.Assumed);
        Assert.Contains(AssumedSetting.LunchGap, standing.Assumed);
    }

    [Fact]
    public void TheLineHoldsNoNameOrAddress()
    {
        var line = CalendarSideStanding.Decide(true, new HouseholdSetupRead.Configured(HouseholdData.Setup()), null, TimeZoneInfo.Utc).Line;

        Assert.DoesNotContain("Sam", line, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", line, StringComparison.Ordinal);
    }
}
