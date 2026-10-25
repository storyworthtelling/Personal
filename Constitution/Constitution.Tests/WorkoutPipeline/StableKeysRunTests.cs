using Constitution.Domain.Calendar;
using Constitution.Domain.Delivery;
using Constitution.Domain.Equipment;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.Documents;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// B2 on the real weekly run, played the way it went wrong. Sam trains on Monday, Wednesday and Friday and Monday's run writes
/// all three. The owner then adds a Monday meeting, and the run, which plans the week again from its first instant, no longer has a
/// Monday session. By place among its kind, Wednesday became the first session and Friday the second, so each was held under the
/// key of the day before it: nothing was written, Friday's event was left under a key nobody asked for, and each day's page took
/// its time from an event on another day (Monday's, already over: too late, so no page at all).
/// </summary>
public sealed class StableKeysRunTests : IDisposable
{
    private readonly WeekRig rig = new();

    public void Dispose() => rig.Dispose();

    private static readonly Session Cardio = new(SessionKind.OutdoorCardio, TimeSpan.FromMinutes(35), true, [new("Warm-up walk"), new("Brisk walk"), new("Cool-down walk")]);

    private static string NameOf(string person, int dayOffset) => DeliveredName.For(DeliveryKind.Workout, MondayDay(dayOffset), person);

    private static PlacementKey Key(int ordinal) => PlacementKey.ForWorkout(Sam, SessionKind.OutdoorCardio, PlacementKey.WeekIndexOf(WeekStart), ordinal);

    // Sam's cardio on the given days, 4 pm, as the author hands it over.
    private void SamTrains(params int[] days)
    {
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(days.Select(day => rig.Session(Sam, day, 16, Cardio)).ToArray()));
    }

    private async Task TheFirstWeekIsWrittenOnMonday()
    {
        rig.BothAreEstablished();
        SamTrains(0, 2, 4);
        var first = await rig.PlannedAsync(rig.Request(Est(11, 2, 5, 30)));
        Assert.Equal(3, first.Placement.Created.Count);
        Assert.Equal(DocumentVerdict.Delivered, Assert.Single(first.Documents).Verdict);
    }

    // ---- the replan that dropped Monday ----

    [Fact]
    public async Task ADroppedMondayLeavesWednesdaysAndFridaysEventsBe()
    {
        await TheFirstWeekIsWrittenOnMonday();
        var snapshot = rig.Writer.Snapshot();
        SamTrains(2, 4);

        var report = await rig.PlannedAsync(rig.Request(Est(11, 4, 6, 0)));

        Assert.Empty(report.Placement.Created);
        Assert.Equal(snapshot, rig.Writer.Snapshot());
        Assert.Equal([Key(1), Key(2)], report.Placement.Plan!.Held.Select(held => held.Key).Order(Comparer<PlacementKey>.Create((a, b) => string.CompareOrdinal(a.Value, b.Value))));
        Assert.Empty(report.Placement.Plan.UncertainKinds);
    }

    [Fact]
    public async Task WednesdaysPageComesFromWednesdaysEvent()
    {
        await TheFirstWeekIsWrittenOnMonday();
        SamTrains(2, 4);

        var report = await rig.PlannedAsync(rig.Request(Est(11, 4, 6, 0)));

        var page = Assert.Single(report.Documents);
        Assert.Equal(DocumentVerdict.Delivered, page.Verdict);
        Assert.Equal(MondayDay(2), page.Day);
        Assert.Equal(MondayAt(2, 16), rig.Delivered.Find(NameOf(Sam, 2))!.SlotStart);
        Assert.Contains(NameOf(Sam, 2), rig.Tablet.Titles.Values);
    }

    [Fact]
    public async Task FridaysPageComesFromFridaysEvent()
    {
        await TheFirstWeekIsWrittenOnMonday();
        SamTrains(2, 4);

        var report = await rig.PlannedAsync(rig.Request(Est(11, 6, 6, 0)));

        var page = Assert.Single(report.Documents);
        Assert.Equal(DocumentVerdict.Delivered, page.Verdict);
        Assert.Equal(MondayAt(4, 16), rig.Delivered.Find(NameOf(Sam, 4))!.SlotStart);
        Assert.Empty(report.Placement.Created);
    }

    [Fact]
    public async Task ANewDayInBetweenIsWrittenOnceAndOnlyOnce()
    {
        rig.BothAreEstablished();
        SamTrains(0, 4);
        await rig.PlannedAsync(rig.Request(Est(11, 2, 5, 30)));
        SamTrains(0, 2, 4);

        var second = await rig.PlannedAsync(rig.Request(Est(11, 2, 9, 0)));
        var third = await rig.PlannedAsync(rig.Request(Est(11, 2, 10, 0)));

        // Wednesday is the new session; it takes the number after the highest issued, and Friday keeps the one it had.
        Assert.Equal(Key(2), Assert.Single(second.Placement.Created).Key);
        Assert.Empty(third.Placement.Created);
        Assert.Equal([Key(0), Key(1), Key(2)], rig.Writer.Of(Sam).Select(item => item.Key).Order(Comparer<PlacementKey>.Create((a, b) => string.CompareOrdinal(a.Value, b.Value))));
        Assert.Equal(MondayAt(4, 16), rig.Writer.By(Key(1), Sam)!.Start);
    }

    // ---- the record ----

    [Fact]
    public async Task EachNumberIssuedIsKept()
    {
        await TheFirstWeekIsWrittenOnMonday();

        var record = KeyAssignmentsCodec.Decode(rig.KeyAssignments.Json);

        Assert.True(record.IsReadable);
        Assert.Equal([Key(0), Key(1), Key(2)], record.Assignments.Issues.Select(issue => issue.Key));
    }

    [Fact]
    public async Task AnUnchangedWeekKeepsNothingMoreAndWritesNothing()
    {
        await TheFirstWeekIsWrittenOnMonday();
        var saves = rig.KeyAssignments.Saves;
        var snapshot = rig.Writer.Snapshot();

        var again = await rig.PlannedAsync(rig.Request(Est(11, 2, 6, 0)));

        Assert.Equal(saves, rig.KeyAssignments.Saves);
        Assert.Equal(snapshot, rig.Writer.Snapshot());
        Assert.Empty(again.Placement.Created);
    }

    // ---- a swap, and then a replan ----

    [Fact]
    public async Task ASwapMadeBeforeTheReplanStillLandsOnTheRightDays()
    {
        var lift = new Session(
            SessionKind.Strength, TimeSpan.FromMinutes(40), false,
            [new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId), new Exercise("Wall angels")]);
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Cardio), rig.Session(Sam, 2, 16, Cardio), rig.Session(Sam, 4, 16, lift)));
        var first = await rig.PlannedAsync(rig.Request(Est(11, 2, 5, 30), Sam));

        // Sam swaps Wednesday's cardio with Friday's lift on the tab; then a Monday meeting takes Monday's session out of the plan.
        var (_, overrides) = await OwnerHand.Swaps(rig, OwnerHand.TabShows(rig, first, MondayDay(0), Est(11, 2, 7)), MondayDay(2), MondayDay(4));
        var written = rig.Writer.CreateCalls;
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 2, 16, Cardio), rig.Session(Sam, 4, 16, lift)));

        var wednesday = await rig.PlannedAsync(rig.Request(Est(11, 4, 5, 30), Sam) with { PlanOverrides = overrides });
        var friday = await rig.PlannedAsync(rig.Request(Est(11, 6, 5, 30), Sam) with { PlanOverrides = overrides });

        Assert.Equal(written, rig.Writer.CreateCalls);
        Assert.Empty(wednesday.Placement.Plan!.UncertainKinds);
        Assert.Equal(DocumentVerdict.Delivered, Assert.Single(wednesday.Documents).Verdict);
        Assert.Contains("Squat", TextOf(rig.Tablet.PdfOf(NameOf(Sam, 2))), StringComparison.Ordinal);
        Assert.Equal(DocumentVerdict.Delivered, Assert.Single(friday.Documents).Verdict);
        Assert.Contains("Warm-up", TextOf(rig.Tablet.PdfOf(NameOf(Sam, 4))), StringComparison.Ordinal);
    }

    private static string TextOf(byte[] pdf) =>
        string.Join(" ", PdfReadBack.Open(pdf).GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));

    // ---- a skip after a replan ----

    [Fact]
    public async Task ASkipMarksTheSessionTheOwnerMeantNotAnotherOne()
    {
        rig.BothAreEstablished();
        SamTrains(0, 4);
        await rig.PlannedAsync(rig.Request(Est(11, 2, 5, 30)));
        Assert.Equal(2, rig.Writer.Of(Sam).Count);

        // A gap opens on Wednesday, so the re-plan has three sessions; the owner skips the new one.
        SamTrains(0, 2, 4);
        var skip = new PlanOverrides([], [new SessionSkipped(Sam, MondayDay(2))]);
        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 9, 0)) with { PlanOverrides = skip });

        Assert.Empty(report.Placement.Created);
        Assert.Equal(2, rig.Writer.Of(Sam).Count);
        var wednesday = new PlacementIndex(report.PlacedPlan, KeyAssignmentsCodec.Decode(rig.KeyAssignments.Json).Assignments).KeyFor(Sam, MondayDay(2))!.Value;
        Assert.Equal(Key(2), wednesday);
        Assert.True(rig.Keys.Load().Keys.Contains(wednesday));
        Assert.Contains(Key(1), report.Placement.Plan!.Held.Select(held => held.Key));
        Assert.Contains(wednesday, report.Placement.Plan.Skipped.Select(skipped => skipped.Key));
    }

    // ---- when the record cannot be trusted ----

    [Fact]
    public async Task AnUnreadableRecordOfNumbersPlacesNothingAndPullsNoPage()
    {
        await TheFirstWeekIsWrittenOnMonday();
        var snapshot = rig.Writer.Snapshot();
        rig.KeyAssignments.Json = "{ not the record";

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 9, 0)));

        Assert.Null(report.Placement.Plan);
        Assert.Contains("issued", report.Placement.BlockedBecause, StringComparison.Ordinal);
        Assert.Equal(snapshot, rig.Writer.Snapshot());
        Assert.Equal(DocumentVerdict.NotOnCalendar, Assert.Single(report.Documents).Verdict);
        Assert.Equal([NameOf(Sam, 0)], rig.Tablet.Titles.Values);
        Assert.Equal("{ not the record", rig.KeyAssignments.Json);
    }

    [Fact]
    public async Task ALostRecordAndAShiftedWeekDecidesNothingAndSaysSo()
    {
        await TheFirstWeekIsWrittenOnMonday();
        var snapshot = rig.Writer.Snapshot();
        rig.KeyAssignments.Json = null;
        SamTrains(2, 4);

        var report = await rig.PlannedAsync(rig.Request(Est(11, 4, 6, 0)));

        Assert.Empty(report.Placement.Created);
        Assert.Equal(snapshot, rig.Writer.Snapshot());
        var unknown = Assert.Single(report.Placement.Plan!.UncertainKinds);
        Assert.Equal(SessionKind.OutdoorCardio, unknown.Kind);
        Assert.Equal(DocumentVerdict.NotOnCalendar, Assert.Single(report.Documents).Verdict);
        Assert.DoesNotContain(NameOf(Sam, 2), rig.Tablet.Titles.Values);
        Assert.Null(rig.KeyAssignments.Json);
    }

    [Fact]
    public async Task TheTrailSaysHowManyEventsWereNotKnown()
    {
        await TheFirstWeekIsWrittenOnMonday();
        rig.KeyAssignments.Json = null;
        SamTrains(2, 4);

        await rig.PlannedAsync(rig.Request(Est(11, 4, 6, 0)));

        Assert.Contains(rig.Log.Phrases, phrase => phrase.StartsWith("workout week planned", StringComparison.Ordinal) && phrase.EndsWith(", 2 events not known", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AWeekThatIsKnownLeavesTheTrailAsItWas()
    {
        await TheFirstWeekIsWrittenOnMonday();

        Assert.DoesNotContain(rig.Log.Phrases, phrase => phrase.Contains("not known", StringComparison.Ordinal));
    }

    // ---- the week ----

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public async Task ABadWeekStartIsRefusedBeforeAnyRead(int dayOffset)
    {
        var request = rig.Request() with { WeekStart = WeekStart.AddDays(dayOffset) };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => rig.Run.RunAsync(request, CancellationToken.None));

        Assert.Contains("Sunday", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, rig.Calendars.Calls);
        Assert.Equal(0, rig.Writer.FindCalls);
        Assert.Empty(rig.Tablet.Calls);
    }
}
