using Constitution.Domain.Calendar;
using Constitution.Domain.Delivery;
using Constitution.Domain.Ink;
using Constitution.Domain.Equipment;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// F-2.2 / F-5: the weekly run end to end over fakes, on the invented household. The planning half: what it reads, what it
/// plans, what it refuses to plan, and that running it again on an unchanged week changes nothing.
/// </summary>
public sealed class WorkoutWeekRunTests : IDisposable
{
    private readonly WeekRig rig = new();

    public void Dispose() => rig.Dispose();

    private static IEnumerable<PlannedSession> Sessions(HouseholdWorkoutPlan plan) =>
        plan.Plans.SelectMany(week => week.Sessions).Concat(plan.JointSessions).Distinct();

    private static Session Strength(params Exercise[] exercises) => new(SessionKind.Strength, TimeSpan.FromMinutes(40), false, exercises);

    private static readonly Session Cardio = new(SessionKind.OutdoorCardio, TimeSpan.FromMinutes(35), true, [new("Warm-up walk"), new("Brisk walk"), new("Cool-down walk")]);

    // ---- what it reads ----

    [Fact]
    public async Task ItReadsEveryCalendarForTheWholeWeek()
    {
        await rig.PlannedAsync();

        Assert.Equal(1, rig.Calendars.Calls);
        Assert.Equal(At(0, 0), rig.Calendars.From);
        Assert.Equal(At(7, 0), rig.Calendars.To);
    }

    [Fact]
    public async Task AFirstInstallPlansTheAssessmentWeek()
    {
        var report = await rig.PlannedAsync();

        Assert.All(new[] { Sam, Lee }, person =>
        {
            Assert.Equal(WorkoutPhase.Assessment, report.Phases[person].Phase);
            Assert.Equal(PhaseReason.FirstInstall, report.Phases[person].Reason);
        });
        Assert.NotEmpty(Sessions(report.Plan));
        Assert.Empty(report.Plan.JointSessions);
        Assert.All(Sessions(report.Plan), session => Assert.Single(session.Participants));
        Assert.Null(report.Origin);
    }

    [Fact]
    public async Task AnEstablishedPairGetTheOrdinaryWeek()
    {
        rig.BothAreEstablished();

        var report = await rig.PlannedAsync();

        Assert.All(new[] { Sam, Lee }, person => Assert.Equal(WorkoutPhase.Moderate, report.Phases[person].Phase));
        Assert.Equal(PlanOrigin.RulesOnly, report.Origin);
        Assert.NotEmpty(Sessions(report.Plan));
    }

    [Fact]
    public async Task EachAssessmentWeekIsRecordedForWhoGotSessions()
    {
        rig.Calendars.Snapshot = Calendars(lee: BlockDays(0, 1, 2, 3, 4, 5, 6));

        var report = await rig.PlannedAsync();

        Assert.NotEmpty(report.Plan.PlanFor(Sam)!.Sessions);
        Assert.Empty(report.Plan.PlanFor(Lee)!.Sessions);
        Assert.Equal([(Sam, WeekStart)], rig.Learning.AssessmentWeeks);
    }

    [Fact]
    public async Task AnUnderwayAssessmentIsNotRecordedAgain()
    {
        await rig.PlannedAsync();
        var writes = rig.Learning.AssessmentWeeks.Count;

        await rig.PlannedAsync(rig.Request(Est(11, 2, 13)));

        Assert.Equal(2, writes);
        Assert.Equal(writes, rig.Learning.AssessmentWeeks.Count);
    }

    // ---- what it refuses to plan ----

    [Fact]
    public async Task NoCalendarReadMeansNothingIsPlanned()
    {
        rig.Calendars.Snapshot = CalendarsWith(
            sam: CalendarRead.Unreadable("Google would not answer."),
            lee: CalendarRead.Unreadable("Google would not answer."),
            family: CalendarRead.Unreadable("Google would not answer."));

        var outcome = await rig.Run.RunAsync(rig.Request(), CancellationToken.None);

        var none = Assert.IsType<WorkoutWeekOutcome.NotPlanned>(outcome);
        Assert.Equal(NotPlannedBecause.CalendarsUnreadable, none.Because);
        Assert.NotEmpty(none.Unreadable);
        Assert.Equal(0, rig.Writer.CreateCalls);
        Assert.Equal(0, rig.Writer.FindCalls);
        Assert.Empty(rig.Tablet.Calls);
        Assert.Empty(rig.Learning.AssessmentWeeks);
        Assert.Empty(rig.Expected.Outstanding());
        Assert.Equal(["workout week not planned, calendars unreadable"], rig.Log.Phrases);
    }

    [Fact]
    public async Task AFamilyCalendarThatCannotBeReadStopsEveryone()
    {
        rig.Calendars.Snapshot = CalendarsWith(family: CalendarRead.Unreadable("The family calendar is not shared."));

        var outcome = await rig.Run.RunAsync(rig.Request(), CancellationToken.None);

        var none = Assert.IsType<WorkoutWeekOutcome.NotPlanned>(outcome);
        Assert.Equal(NotPlannedBecause.CalendarsUnreadable, none.Because);
        Assert.Contains(none.Unreadable, item => item.Source == CalendarSource.Family);
    }

    [Fact]
    public async Task OneAdultsCalendarUnreadablePlansTheOther()
    {
        rig.Calendars.Snapshot = CalendarsWith(lee: CalendarRead.Unreadable("Lee has not shared a calendar."));

        var report = await rig.PlannedAsync();

        Assert.Equal([Lee], report.PeopleWithUnreadableCalendars);
        Assert.NotEmpty(report.Plan.PlanFor(Sam)!.Sessions);
        Assert.Empty(report.Plan.PlanFor(Lee)!.Sessions);
        Assert.NotNull(report.Plan.PlanFor(Lee)!.NothingBecause);
        Assert.DoesNotContain(rig.Writer.All, item => item.Person == Lee);
    }

    [Fact]
    public async Task AnUnreadableLedgerPlansNothing()
    {
        rig.Learning.Held = WorkoutLearning.Nothing with { UnreadableBecause = "The skip record could not be read." };

        var outcome = await rig.Run.RunAsync(rig.Request(), CancellationToken.None);

        var none = Assert.IsType<WorkoutWeekOutcome.NotPlanned>(outcome);
        Assert.Equal(NotPlannedBecause.RecordsUnreadable, none.Because);
        Assert.Equal(0, rig.Calendars.Calls);
        Assert.Equal(0, rig.Writer.CreateCalls);
        Assert.Empty(rig.Tablet.Calls);
    }

    [Fact]
    public async Task ARulesObjectWithNoAdultsPlansNothing()
    {
        var request = rig.Request() with { Rules = Rules() with { Adults = [] } };

        var outcome = await rig.Run.RunAsync(request, CancellationToken.None);

        Assert.Equal(NotPlannedBecause.NoOneToPlan, Assert.IsType<WorkoutWeekOutcome.NotPlanned>(outcome).Because);
        Assert.Equal(0, rig.Calendars.Calls);
    }

    [Fact]
    public async Task NamingAStrangerToDeliverForIsRefusedFirst()
    {
        var request = rig.Request(null, "Robin");

        await Assert.ThrowsAsync<ArgumentException>(() => rig.Run.RunAsync(request, CancellationToken.None));

        Assert.Equal(0, rig.Calendars.Calls);
        Assert.Equal(0, rig.Learning.Loads);
    }

    [Fact]
    public async Task APageAheadOfNoDaysIsRefused()
    {
        var request = rig.Request() with { DeliverAheadDays = -1 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => rig.Run.RunAsync(request, CancellationToken.None));
        Assert.Equal(0, rig.Calendars.Calls);
    }

    // ---- the week is planned as of its start ----

    [Fact]
    public async Task TheSameWeekComesOutWhateverTheHour()
    {
        rig.BothAreEstablished();
        var morning = await rig.PlannedAsync(rig.Request(Est(11, 2, 5, 30)));
        var afternoon = await rig.PlannedAsync(rig.Request(Est(11, 2, 13)));
        var thursday = await rig.PlannedAsync(rig.Request(Est(11, 5, 9)));

        Assert.Equal(Describe(morning.PlacedPlan), Describe(afternoon.PlacedPlan));
        Assert.Equal(Describe(morning.PlacedPlan), Describe(thursday.PlacedPlan));
        Assert.NotEmpty(Describe(morning.PlacedPlan));
    }

    private static string Describe(HouseholdWorkoutPlan plan) => string.Join(
        "\n", plan.Plans.SelectMany(week => week.Sessions.Select(session => $"{week.Person}|{session.Start:O}|{session.Session.Kind}|{session.Session.Duration}")));

    // ---- placing ----

    [Fact]
    public async Task EverySessionOfTheWeekGetsItsEvent()
    {
        rig.BothAreEstablished();

        var report = await rig.PlannedAsync();

        var wanted = report.PlacedPlan.Plans.Sum(week => week.Sessions.Count);
        Assert.True(wanted >= 4, "The invented week should hold at least four sessions.");
        Assert.Equal(wanted, report.Placement.Created.Count);
        Assert.Equal(wanted, rig.Writer.All.Count);
        Assert.Empty(report.Placement.Failed);
        Assert.All(rig.Writer.All, item => Assert.Equal("Workout · " + (int)(WorkoutLength(report, item)) + " min", item.Title));
    }

    private static double WorkoutLength(WorkoutWeekReport report, FakeCalendar.Stored item) =>
        report.PlacedPlan.PlanFor(item.Person)!.Sessions.First(session => session.Start == item.Start).Session.Duration.TotalMinutes;

    [Fact]
    public async Task TheEventsAreKeyedTheWayThePlanIsIndexed()
    {
        rig.BothAreEstablished();

        var report = await rig.PlannedAsync();

        var index = new PlacementIndex(report.PlacedPlan);
        foreach (var week in report.PlacedPlan.Plans)
        {
            foreach (var session in week.Sessions)
            {
                Assert.NotNull(rig.Writer.By(index.KeyFor(week.Person, session.Day)!.Value, week.Person));
            }
        }
    }

    [Fact]
    public async Task AReRunOnAnUnchangedWeekChangesNothing()
    {
        rig.BothAreEstablished();
        await rig.PlannedAsync();
        var calendar = rig.Writer.Snapshot();
        var creates = rig.Writer.CreateCalls;
        var saves = rig.Keys.Saves;
        var uploads = rig.Tablet.Uploads;
        var titles = rig.Tablet.Titles.Values.Order().ToList();
        var expected = File.ReadAllText(Path.Combine(rig.Folder.FullName, "expected.json"));
        var delivered = File.ReadAllText(Path.Combine(rig.Folder.FullName, "delivered.json"));

        for (var again = 1; again <= 3; again++)
        {
            var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 5, 30).AddMinutes(10 * again)));
            Assert.All(report.Documents, item => Assert.Equal(DocumentVerdict.Unchanged, item.Verdict));
        }

        Assert.Equal(calendar, rig.Writer.Snapshot());
        Assert.Equal(creates, rig.Writer.CreateCalls);
        Assert.Equal(saves, rig.Keys.Saves);
        Assert.Equal(uploads, rig.Tablet.Uploads);
        Assert.Equal(titles, rig.Tablet.Titles.Values.Order().ToList());
        Assert.Equal(expected, File.ReadAllText(Path.Combine(rig.Folder.FullName, "expected.json")));
        Assert.Equal(delivered, File.ReadAllText(Path.Combine(rig.Folder.FullName, "delivered.json")));
        Assert.DoesNotContain(rig.Tablet.Calls, call => call.StartsWith("trash:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALateFirstRunNeverWritesIntoThePast()
    {
        rig.BothAreEstablished();
        var wednesdayMorning = Est(11, 4, 10);

        var report = await rig.PlannedAsync(rig.Request(wednesdayMorning));

        Assert.NotEmpty(rig.Writer.All);
        Assert.All(rig.Writer.All, item => Assert.True(item.Start >= wednesdayMorning, "An event was written for a time that had gone."));
        Assert.Contains(report.Placement.Failed, item => item.Reason == PastWriteGuard.Reason);
        Assert.All(report.Placement.Failed, item => Assert.True(item.Event.Start < wednesdayMorning));
    }

    [Fact]
    public async Task ASessionTheOwnerDeletedIsNotPlacedAgain()
    {
        rig.BothAreEstablished();
        var first = await rig.PlannedAsync();
        var session = first.PlacedPlan.PlanFor(Sam)!.Sessions[0];
        var key = new PlacementIndex(first.PlacedPlan).KeyFor(Sam, session.Day)!.Value;
        rig.Writer.OwnerDeletes(key, Sam);
        var creates = rig.Writer.CreateCalls;

        var second = await rig.PlannedAsync(rig.Request(Est(11, 2, 6)));

        Assert.Equal(creates, rig.Writer.CreateCalls);
        Assert.Contains(second.Placement.Plan!.Skipped, item => item.Key == key);
    }

    [Fact]
    public async Task ThePlannedWeekIsPlacedNotTheAdaptedOne()
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Cardio)));
        rig.Conditions.Read = ConditionsRead.Of(RainAcross(0, 16, 18));

        var report = await rig.PlannedAsync();

        Assert.Equal(SessionKind.OutdoorCardio, report.PlacedPlan.PlanFor(Sam)!.Sessions.Single().Session.Kind);
        Assert.Equal(SessionKind.Treadmill, report.Plan.PlanFor(Sam)!.Sessions.Single().Session.Kind);
        Assert.Single(rig.Writer.All);
    }

    [Fact]
    public async Task AdaptedPlacementAddsASecondEventOnAFlip()
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Cardio)));
        var request = rig.Request() with { PlaceAdaptedWeek = true };
        await rig.PlannedAsync(request);
        Assert.Single(rig.Writer.All);

        rig.Conditions.Read = ConditionsRead.Of(RainAcross(0, 16, 18));
        await rig.PlannedAsync(request with { Now = Est(11, 2, 6) });

        Assert.Equal(2, rig.Writer.All.Count);
    }

    internal static IEnumerable<HourConditions> RainAcross(int dayOffset, int fromHour, int toHour) =>
        Enumerable.Range(fromHour, toHour - fromHour).Select(hour => new HourConditions(At(dayOffset, hour), 48, 90, PrecipitationKind.Rain, 30, true));

    // ---- the adaptations, in the order each documented ----

    [Fact]
    public async Task AForecastThatCannotBeReadChangesNothing()
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Cardio)));
        rig.Conditions.Read = ConditionsRead.Unavailable("The forecast did not answer.");

        var report = await rig.PlannedAsync();

        Assert.False(report.ConditionsWereRead);
        Assert.Empty(report.ConditionChanges);
        Assert.Equal(SessionKind.OutdoorCardio, report.Plan.PlanFor(Sam)!.Sessions.Single().Session.Kind);
    }

    [Fact]
    public async Task RainMovesTheWalkIndoorsAndTheReportSaysWhy()
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Cardio)));
        rig.Conditions.Read = ConditionsRead.Of(RainAcross(0, 16, 18));

        var report = await rig.PlannedAsync();

        Assert.True(report.ConditionsWereRead);
        var change = Assert.Single(report.ConditionChanges);
        Assert.Equal(ConditionAction.MovedIndoors, change.Action);
        Assert.Equal("Moved indoors: rain forecast", change.Line);
    }

    [Fact]
    public async Task UnderRecoveryMakesTodayMobility()
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Strength(new Exercise("Sit to stand from a chair")))));
        rig.Recovery.Reads[Sam] = new RecoveryRead(RecoveryState.UnderRecovered, [RecoveryReason.ShortSleep], [], []);

        var report = await rig.PlannedAsync();

        var swap = Assert.Single(report.RecoverySwaps);
        Assert.Equal(SessionKind.Strength, swap.Was);
        Assert.Equal(SessionKind.Mobility, report.Plan.PlanFor(Sam)!.Sessions.Single().Session.Kind);
        Assert.Equal(SessionKind.Strength, report.PlacedPlan.PlanFor(Sam)!.Sessions.Single().Session.Kind);
    }

    [Fact]
    public async Task AnOpenSorenessWorksTheSessionAround()
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Strength(new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId), new Exercise("Wall angels")))));
        rig.Learning.Held = rig.Learning.Held with { Soreness = SorenessLedger.Empty.Note(Sam, "my left knee", new DateOnly(2026, 10, 31)) };

        var report = await rig.PlannedAsync();

        var adjustment = Assert.Single(report.SorenessAdjustments);
        Assert.Equal(Sam, adjustment.Person);
        Assert.True(adjustment.EasyTempo);
    }

    [Fact]
    public async Task AFamilyHolidayPausesTheDaysItCovers()
    {
        rig.BothAreEstablished();
        rig.Calendars.Snapshot = Calendars(family: [CalendarEntry.AllDay("Family holiday", Day(0), Day(3))]);

        var report = await rig.PlannedAsync();

        Assert.All(new[] { Sam, Lee }, person =>
            Assert.DoesNotContain(report.Plan.PlanFor(person)!.Sessions, session => session.Day < Day(3)));
        Assert.NotEmpty(report.DayNotes);
        Assert.All(report.Documents, item => Assert.True(item.Day >= Day(3)));
    }

    [Fact]
    public async Task AHolidayMarkedFreeStillPausesThePlan()
    {
        // Why the calendar seam must leave a declined or cancelled holiday out rather than hand it over as free: trips are
        // read whatever an entry's availability says, so a "free" holiday is still a holiday.
        rig.BothAreEstablished();
        rig.Calendars.Snapshot = Calendars(family: [CalendarEntry.AllDay("Family holiday", Day(0), Day(3), Availability.Free)]);

        var report = await rig.PlannedAsync();

        Assert.All(new[] { Sam, Lee }, person =>
            Assert.DoesNotContain(report.Plan.PlanFor(person)!.Sessions, session => session.Day < Day(3)));
    }

    // ---- every stage of the pipeline is really in it ----

    [Fact]
    public async Task LastWeeksSkipsEaseThePlan()
    {
        rig.BothAreEstablished();
        var steady = await rig.PlannedAsync();
        var skipped = rig.Learning.Held.Skips
            .Settle(new SessionMarks(Sam, new DateOnly(2026, 10, 27), SessionKind.Strength, ["done"]), [new InkResolution.Skipped("done")])
            .Settle(new SessionMarks(Sam, new DateOnly(2026, 10, 29), SessionKind.Mobility, ["done"]), [new InkResolution.Skipped("done")]);
        rig.Learning.Held = rig.Learning.Held with { Skips = skipped };

        var eased = await rig.PlannedAsync(rig.Request(Est(11, 2, 6)));

        Assert.True(
            eased.PlacedPlan.PlanFor(Sam)!.Sessions.Count < steady.PlacedPlan.PlanFor(Sam)!.Sessions.Count,
            "Two skipped sessions last week should leave fewer sessions this week.");
    }

    [Fact]
    public async Task AStoppedEarlySessionTrimsTheNextStrengthPage()
    {
        rig.BothAreEstablished();
        Exercise[] moves = [new("Sit to stand from a chair"), new("Band row", EquipmentCatalogue.BandsId), new("Wall angels"), new("Hip hinge with hands on hips")];
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Strength(moves))));
        var pages = new SessionPages(Sam, new DateOnly(2026, 10, 28), SessionKind.Strength, moves.Select(move => new ExercisePageRef(move, "done-" + move.Name, "feel")).ToList());
        var stopped = ProgressionLedger.Empty.Settle(pages, [
            new InkResolution.Ticked("done-" + moves[0].Name),
            new InkResolution.Ticked("done-" + moves[1].Name),
            new InkResolution.Skipped("done-" + moves[2].Name),
            new InkResolution.Skipped("done-" + moves[3].Name)]);
        Assert.Equal(2, stopped.ExerciseCap(Sam));
        rig.Learning.Held = rig.Learning.Held with { Progression = stopped };

        var report = await rig.PlannedAsync();

        Assert.Equal(2, report.Plan.PlanFor(Sam)!.Sessions.Single().Session.Exercises.Count);
        Assert.Equal(4, report.PlacedPlan.PlanFor(Sam)!.Sessions.Single().Session.Exercises.Count);
    }

    [Fact]
    public async Task AWorkTripRebuildsTheSessionForTheRoad()
    {
        rig.BothAreEstablished();
        rig.Calendars.Snapshot = Calendars(sam: [CalendarEntry.AllDay("Conference", Day(1), Day(2))]);
        var lift = Strength(new Exercise("Band row", EquipmentCatalogue.BandsId), new Exercise("Wall angels"));
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 1, 16, lift)));

        var report = await rig.PlannedAsync();

        var change = Assert.Single(report.TravelChanges);
        Assert.Equal(TravelAction.Rebuilt, change.Action);
        Assert.Equal("On the road: in the room", change.Line);
        Assert.DoesNotContain(report.Plan.PlanFor(Sam)!.Sessions.Single().Session.EquipmentNeeded, id => id == EquipmentCatalogue.BandsId);
        Assert.Single(report.DayNotes);
    }

    [Fact]
    public async Task ATripDoesNotBlockTheDayItsEntrySits()
    {
        rig.BothAreEstablished();
        rig.Calendars.Snapshot = Calendars(sam: [CalendarEntry.AllDay("Conference", Day(1), Day(2)), .. BlockDays(0, 2, 3, 4, 5, 6)]);

        var report = await rig.PlannedAsync();

        Assert.Contains(report.PlacedPlan.PlanFor(Sam)!.Sessions, session => session.Day == Day(1));
    }

    [Fact]
    public async Task TheWeatherIsLeftAloneOnADayWhoeverIsAway()
    {
        rig.BothAreEstablished();
        rig.Calendars.Snapshot = Calendars(sam: [CalendarEntry.AllDay("Conference", Day(0), Day(1))]);
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Cardio)));
        rig.Conditions.Read = ConditionsRead.Of(RainAcross(0, 16, 18));

        var report = await rig.PlannedAsync();

        Assert.Empty(report.ConditionChanges);
        Assert.Equal(SessionKind.OutdoorCardio, report.Plan.PlanFor(Sam)!.Sessions.Single().Session.Kind);
    }

    [Fact]
    public async Task TheShowerRoomIsTheOnePlacementKeeps()
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Cardio)));
        rig.Conditions.Read = ConditionsRead.Of(RainAcross(0, 16, 17));

        var usual = await rig.PlannedAsync();
        var change = Assert.Single(usual.ConditionChanges);
        Assert.Equal(ConditionAction.MovedInTime, change.Action);
        Assert.Equal(At(0, 17), change.Replacement!.Start);

        // With an hour and a half kept for the shower there is no clear start left inside the gap, so the walk goes indoors.
        var request = rig.Request(Est(11, 2, 6)) with { Placement = new PlacementOptions { ShowerBuffer = TimeSpan.FromMinutes(90) } };
        var roomy = await rig.PlannedAsync(request);
        Assert.Equal(ConditionAction.MovedIndoors, Assert.Single(roomy.ConditionChanges).Action);
    }

    [Fact]
    public async Task AssessmentBaselinesSetTheStartingDose()
    {
        rig.BothAreEstablished();
        var lift = Strength(new Exercise("Sit to stand from a chair"), new Exercise("Wall angels"));
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, lift)));
        var baselines = new AssessmentBaselines(new Dictionary<string, decimal> { [AssessmentCatalogue.SitToStandId] = 20m }, new Dictionary<string, string>(), []);
        rig.Learning.Held = rig.Learning.Held with { Baselines = new Dictionary<string, AssessmentBaselines>(StringComparer.OrdinalIgnoreCase) { [Sam] = baselines } };

        await rig.PlannedAsync();

        var text = string.Join(" ", Constitution.Tests.Documents.PdfReadBack.Open(rig.Tablet.PdfOf(DeliveredName.For(DeliveryKind.Workout, Day(0), Sam))).GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));
        Assert.Contains("2 × 10", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReportCarriesTheRequestAsOfNow()
    {
        var report = await rig.PlannedAsync(rig.Request(Est(11, 3, 9)));

        Assert.Equal(Est(11, 3, 9), report.Request.Now);
        Assert.Equal(WeekStart, report.Request.WeekStart);
        Assert.Equal(2, report.Request.People.Count);
    }

    // ---- a failure at one stage does not spoil the next run ----

    [Fact]
    public async Task ATabletBugAfterPlacementIsPickedUpNextRun()
    {
        rig.BothAreEstablished();
        rig.Tablet.UploadFails = new InvalidOperationException("a bug on the tablet side");

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Run.RunAsync(rig.Request(), CancellationToken.None));
        var written = rig.Writer.All.Count;
        Assert.NotEqual(0, written);
        Assert.Empty(rig.Expected.Outstanding());
        rig.Tablet.UploadFails = null;

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 6)));

        Assert.Equal(written, rig.Writer.All.Count);
        Assert.NotEmpty(report.Documents);
        Assert.All(report.Documents, item => Assert.Equal(DocumentVerdict.Delivered, item.Verdict));
        Assert.Equal(report.Documents.Count, rig.Expected.Outstanding().Count);
    }

    [Fact]
    public async Task TheLogTellsTheRunInWordsAndCounts()
    {
        rig.BothAreEstablished();

        var report = await rig.PlannedAsync();

        Assert.Contains(rig.Log.Phrases, phrase => phrase.StartsWith("workout week planned, ", StringComparison.Ordinal));
        Assert.Equal(report.Documents.Count, rig.Log.Phrases.Count(phrase => phrase.StartsWith("workout document", StringComparison.Ordinal)));
        Assert.Contains("workout plan: rules only (not configured)", rig.Log.Phrases);
    }
}
