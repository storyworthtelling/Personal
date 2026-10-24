using Constitution.Domain.Delivery;
using Constitution.Domain.Equipment;
using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Domain.Workouts.Screens;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.Documents;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The Workouts tab moves a session by editing its calendar event; the weekly run re-derives its base plan every time. Told
/// what the owner did, the run plans the week they left: the moved session keeps its event and its page follows it, a skipped
/// one gets neither, and a run on a week that has not changed changes nothing.
/// </summary>
public sealed class PlanOverridesRunTests : IDisposable
{
    private readonly WeekRig rig = new();

    private readonly Session lift = new(
        SessionKind.Strength, TimeSpan.FromMinutes(40), false,
        [new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId), new Exercise("Wall angels")]);

    private readonly Session walk = new(SessionKind.OutdoorCardio, TimeSpan.FromMinutes(35), true, [new("Warm-up walk"), new("Brisk walk"), new("Cool-down walk")]);

    public PlanOverridesRunTests()
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, lift), rig.Session(Sam, 2, 16, walk)));
    }

    public void Dispose() => rig.Dispose();

    private static string NameOf(int dayOffset) => DeliveredName.For(DeliveryKind.Workout, Day(dayOffset), Sam);

    private static DateTimeOffset MonSeven => Est(11, 2, 7);

    private WorkoutWeekRequest Asked(DateTimeOffset now, PlanOverrides? overrides = null) =>
        rig.Request(now, Sam) with { PlanOverrides = overrides ?? PlanOverrides.None };

    private static string TextOf(byte[] pdf) =>
        string.Join(" ", PdfReadBack.Open(pdf).GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));

    private async Task<(SwapResult Swap, PlanOverrides Overrides)> OwnerSwaps(WorkoutWeekReport first, DateTimeOffset now, int fromDay, int toDay) =>
        await OwnerHand.Swaps(rig, OwnerHand.TabShows(rig, first, Day(0), now), Day(fromDay), Day(toDay));

    [Fact]
    public async Task AMoveToARestDayTakesThePageOff()
    {
        var first = await rig.PlannedAsync(Asked(Est(11, 2, 5, 30)));
        Assert.Contains(NameOf(0), rig.Tablet.Titles.Values);
        var (_, overrides) = await OwnerSwaps(first, MonSeven, 0, 4);
        var created = rig.Writer.CreateCalls;

        var second = await rig.PlannedAsync(Asked(MonSeven, overrides));

        var outcome = Assert.Single(second.Documents);
        Assert.Equal(DocumentVerdict.Withdrawn, outcome.Verdict);
        Assert.DoesNotContain(NameOf(0), rig.Tablet.Titles.Values);
        Assert.Equal(created, rig.Writer.CreateCalls);
    }

    [Fact]
    public async Task TheNewDayGetsItsPageAndNoSecondEvent()
    {
        var first = await rig.PlannedAsync(Asked(Est(11, 2, 5, 30)));
        var (swap, overrides) = await OwnerSwaps(first, MonSeven, 0, 4);
        var created = rig.Writer.CreateCalls;
        var friday = swap.Moves.Single(move => move.New.Day == Day(4)).New;

        var friMorning = await rig.PlannedAsync(Asked(Est(11, 6, 5, 30), overrides));

        var outcome = Assert.Single(friMorning.Documents);
        Assert.Equal(DocumentVerdict.Delivered, outcome.Verdict);
        Assert.Equal(NameOf(4), outcome.Name);
        Assert.Contains("Squat", TextOf(rig.Tablet.PdfOf(NameOf(4))), StringComparison.Ordinal);
        Assert.Equal(created, rig.Writer.CreateCalls);
        Assert.Equal(friday.Start, rig.Writer.Of(Sam).Single(item => item.Start.Date == friday.Start.Date).Start);
    }

    [Fact]
    public async Task ASwapOfKindsSwapsTheirPages()
    {
        var first = await rig.PlannedAsync(Asked(Est(11, 2, 5, 30)));
        var (_, overrides) = await OwnerSwaps(first, MonSeven, 0, 2);
        var created = rig.Writer.CreateCalls;

        var monday = await rig.PlannedAsync(Asked(MonSeven, overrides));
        var wednesday = await rig.PlannedAsync(Asked(Est(11, 4, 5, 30), overrides));

        Assert.Equal(DocumentVerdict.Replaced, Assert.Single(monday.Documents).Verdict);
        Assert.Contains("Warm-up", TextOf(rig.Tablet.PdfOf(NameOf(0))), StringComparison.Ordinal);
        Assert.Equal(DocumentVerdict.Delivered, Assert.Single(wednesday.Documents).Verdict);
        Assert.Contains("Squat", TextOf(rig.Tablet.PdfOf(NameOf(2))), StringComparison.Ordinal);
        Assert.Equal(created, rig.Writer.CreateCalls);
    }

    [Fact]
    public async Task AnUnchangedWeekWithMovesChangesNothing()
    {
        var first = await rig.PlannedAsync(Asked(Est(11, 2, 5, 30)));
        var (_, overrides) = await OwnerSwaps(first, MonSeven, 0, 4);
        await rig.PlannedAsync(Asked(MonSeven, overrides));
        var calendar = rig.Writer.Snapshot();
        var titles = rig.Tablet.Titles.Values.Order().ToList();
        var uploads = rig.UploadedTitles.Count();
        var outstanding = rig.Expected.Outstanding().Select(item => item.Document.Name).Order().ToList();

        var again = await rig.PlannedAsync(Asked(MonSeven.AddMinutes(5), overrides));

        Assert.Equal(calendar, rig.Writer.Snapshot());
        Assert.Equal(titles, rig.Tablet.Titles.Values.Order().ToList());
        Assert.Equal(uploads, rig.UploadedTitles.Count());
        Assert.Equal(outstanding, rig.Expected.Outstanding().Select(item => item.Document.Name).Order().ToList());
        Assert.DoesNotContain(again.Documents, item => item.Verdict is DocumentVerdict.Delivered or DocumentVerdict.Replaced);
    }

    [Fact]
    public async Task ASkippedSessionGetsNoEventOrPage()
    {
        var skip = new PendingSkip(Sam, Day(0), SessionKind.Strength, rig.Session(Sam, 0, 16, lift), null, null, MonSeven);
        var overrides = PlanOverrides.From(new WorkoutsChange.SkipCommitted(skip));

        var report = await rig.PlannedAsync(Asked(Est(11, 2, 5, 30), overrides));

        Assert.DoesNotContain(rig.Writer.Of(Sam), item => item.Start.Date == Est(11, 2, 12).Date);
        Assert.Single(rig.Writer.Of(Sam));
        Assert.Empty(rig.Tablet.Titles);
        Assert.DoesNotContain(report.Documents, item => item.Verdict is DocumentVerdict.Delivered or DocumentVerdict.Replaced);
    }

    [Fact]
    public async Task ASkipTakesBackAPageAlreadySent()
    {
        await rig.PlannedAsync(Asked(Est(11, 2, 5, 30)));
        Assert.Contains(NameOf(0), rig.Tablet.Titles.Values);
        var skip = new PendingSkip(Sam, Day(0), SessionKind.Strength, rig.Session(Sam, 0, 16, lift), null, null, MonSeven);
        var overrides = PlanOverrides.From(new WorkoutsChange.SkipCommitted(skip));

        var report = await rig.PlannedAsync(Asked(MonSeven, overrides));

        Assert.Equal(DocumentVerdict.Withdrawn, Assert.Single(report.Documents).Verdict);
        Assert.DoesNotContain(NameOf(0), rig.Tablet.Titles.Values);
    }

    [Fact]
    public async Task WithNoMovesTheRunIsWhatItWas()
    {
        var plain = await rig.PlannedAsync(Asked(Est(11, 2, 5, 30)));

        Assert.Equal(DocumentVerdict.Delivered, Assert.Single(plain.Documents).Verdict);
        Assert.Equal(2, rig.Writer.Of(Sam).Count);
    }

    [Fact]
    public async Task TheReportCarriesThePlanWithTheMoves()
    {
        var first = await rig.PlannedAsync(Asked(Est(11, 2, 5, 30)));
        var (swap, overrides) = await OwnerSwaps(first, MonSeven, 0, 4);

        var second = await rig.PlannedAsync(Asked(MonSeven, overrides));

        var sessions = second.PlacedPlan.PlanFor(Sam)!.Sessions;
        Assert.DoesNotContain(sessions, item => item.Day == Day(0));
        Assert.Equal(Day(4), sessions.Single(item => item.Session.Kind == SessionKind.Strength).Day);
        Assert.Equal(swap.Moves[0].New.Start, sessions.Single(item => item.Session.Kind == SessionKind.Strength).Start);
    }
}
