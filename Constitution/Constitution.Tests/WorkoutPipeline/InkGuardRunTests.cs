using Constitution.Domain.Delivery;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// S3 on the real run. A page already on the tablet is replaced before its slot, or taken off if its session is called off. If the
/// owner has begun writing on it, or nobody can tell, it stays: the read-back reads a blank page as a skip, so a blank copy set down over
/// a begun session is a skip the owner never made. And with the record of what was delivered gone, the first send finds the page on the
/// tablet instead of uploading a second one under the same title.
/// </summary>
public sealed class InkGuardRunTests : IDisposable
{
    private readonly WeekRig rig = new();

    public void Dispose() => rig.Dispose();

    private static readonly Session Cardio = new(SessionKind.OutdoorCardio, TimeSpan.FromMinutes(35), true, [new("Warm-up walk"), new("Brisk walk"), new("Cool-down walk")]);

    private static string NameOf(string person, int dayOffset = 0) => DeliveryNaming.NameOf(person, dayOffset);

    // Sam has one outdoor cardio session, Monday at 4 pm.
    private void SamWalksMonday()
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Cardio)));
    }

    // A note about a sore shoulder changes what the page says, so the run wants to replace it.
    private void ASoreShoulderIsNoted() =>
        rig.Learning.Held = rig.Learning.Held with { Soreness = SorenessLedger.Empty.Note(Sam, "my left shoulder", new DateOnly(2026, 11, 2)) };

    // ---- replacing ----

    [Fact]
    public async Task AnInkedPageIsNotReplacedBeforeTheSlot()
    {
        SamWalksMonday();
        await rig.PlannedAsync();
        var onTheGlass = rig.Tablet.PdfOf(NameOf(Sam));
        var recorded = rig.Delivered.Find(NameOf(Sam));
        rig.InkProbe.Answer = InkProbe.HasInk;
        ASoreShoulderIsNoted();

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 15, 59)));

        var page = Assert.Single(report.Documents);
        Assert.Equal(DocumentVerdict.Kept, page.Verdict);
        Assert.Equal(PageInk.HasInk, page.Ink);
        Assert.Equal(onTheGlass, rig.Tablet.PdfOf(NameOf(Sam)));
        Assert.Equal(1, rig.Tablet.Uploads);
        Assert.DoesNotContain(rig.Tablet.Calls, call => call.StartsWith("trash:", StringComparison.Ordinal));
        Assert.Equal(recorded, rig.Delivered.Find(NameOf(Sam)));
        Assert.Single(rig.Expected.Outstanding());
        Assert.Contains("workout document kept, ink on the page", rig.Log.Phrases);
    }

    [Fact]
    public async Task APageThatCannotBeCheckedIsNotReplacedAndTheTrailNamesTheFailure()
    {
        SamWalksMonday();
        await rig.PlannedAsync();
        rig.InkProbe.Answer = InkProbe.Unknown(new HttpRequestException("503"));
        ASoreShoulderIsNoted();

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 15, 59)));

        var page = Assert.Single(report.Documents);
        Assert.Equal(DocumentVerdict.Kept, page.Verdict);
        Assert.Equal(PageInk.Unknown, page.Ink);
        Assert.IsType<HttpRequestException>(page.Failure);
        Assert.Equal(1, rig.Tablet.Uploads);
        Assert.Contains("workout document kept, ink not known: HttpRequestException", rig.Log.Phrases);
    }

    [Fact]
    public async Task TheNextRunLooksAgainAndReplacesWhenThePageIsBlank()
    {
        SamWalksMonday();
        await rig.PlannedAsync();
        rig.InkProbe.Answer = InkProbe.Unknown();
        ASoreShoulderIsNoted();
        await rig.PlannedAsync(rig.Request(Est(11, 2, 15, 50)));
        rig.InkProbe.Answer = InkProbe.Blank;

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 15, 59)));

        Assert.Equal(DocumentVerdict.Replaced, Assert.Single(report.Documents).Verdict);
        Assert.Equal([NameOf(Sam)], rig.Tablet.Titles.Values);
    }

    // ---- taking a page off ----

    [Fact]
    public async Task AnInkedPageIsNotTakenOffWhenItsSessionIsCalledOff()
    {
        rig.BothAreEstablished();
        var sunday = new Session(SessionKind.GentleWalk, TimeSpan.FromMinutes(30), false, [new Exercise("Easy walk")]);
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, -1, 14, sunday)));
        var request = rig.Request(Est(11, 1, 6)) with { Equipment = EquipmentSet.Of([]) };
        await rig.PlannedAsync(request);
        rig.Conditions.Read = ConditionsRead.Of(WorkoutWeekRunTests.RainAcross(-1, 6, 18));
        rig.InkProbe.Answer = InkProbe.HasInk;

        var report = await rig.PlannedAsync(request with { Now = Est(11, 1, 10) });

        Assert.Equal(DocumentVerdict.Kept, Assert.Single(report.Documents).Verdict);
        Assert.Equal([NameOf(Sam, -1)], rig.Tablet.Titles.Values);
        Assert.NotNull(rig.Delivered.Find(NameOf(Sam, -1)));
        Assert.Single(rig.Expected.Outstanding());
    }

    // ---- the record of what was delivered is gone ----

    [Fact]
    public async Task ALostRecordReplacesABlankPageInsteadOfMakingASecond()
    {
        SamWalksMonday();
        await rig.PlannedAsync();
        rig.Delivered.Remove(NameOf(Sam));

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 9)));

        Assert.Equal(DocumentVerdict.Delivered, Assert.Single(report.Documents).Verdict);
        Assert.Equal([NameOf(Sam)], rig.Tablet.Titles.Values);
        Assert.Equal(2, rig.Tablet.Uploads);
        Assert.Contains(rig.Tablet.Calls, call => call.StartsWith("trash:", StringComparison.Ordinal));
        Assert.NotNull(rig.Delivered.Find(NameOf(Sam)));
    }

    [Fact]
    public async Task ALostRecordOverAnInkedPageKeepsItAndMakesNoSecond()
    {
        SamWalksMonday();
        await rig.PlannedAsync();
        rig.Delivered.Remove(NameOf(Sam));
        rig.InkProbe.Answer = InkProbe.HasInk;

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 9)));

        Assert.Equal(DocumentVerdict.Kept, Assert.Single(report.Documents).Verdict);
        Assert.Equal([NameOf(Sam)], rig.Tablet.Titles.Values);
        Assert.Equal(1, rig.Tablet.Uploads);
        Assert.Null(rig.Delivered.Find(NameOf(Sam)));
    }

    [Fact]
    public async Task ALostRecordOnceTheSlotHasBegunLeavesThePageAlone()
    {
        SamWalksMonday();
        await rig.PlannedAsync();
        rig.Delivered.Remove(NameOf(Sam));

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 16, 20)));

        var page = Assert.Single(report.Documents);
        Assert.Equal(DocumentVerdict.Kept, page.Verdict);
        Assert.Equal(PageInk.Blank, page.Ink);
        Assert.Equal(1, rig.Tablet.Uploads);
        Assert.Empty(rig.InkProbe.Asked);
        Assert.Contains("workout document kept, page already on the tablet", rig.Log.Phrases);
    }

    [Fact]
    public async Task AFirstSendThatCannotLookTheTitleUpUploadsNothingAndTriesAgain()
    {
        SamWalksMonday();
        rig.Tablet.ListFails = new HttpRequestException("503");

        var first = await rig.PlannedAsync();
        Assert.Equal(DocumentVerdict.NotDelivered, Assert.Single(first.Documents).Verdict);
        Assert.Null(rig.Delivered.Find(NameOf(Sam)));
        Assert.Equal(0, rig.Tablet.Uploads);
        rig.Tablet.ListFails = null;
        var second = await rig.PlannedAsync(rig.Request(Est(11, 2, 6)));

        Assert.Equal(DocumentVerdict.Delivered, Assert.Single(second.Documents).Verdict);
        Assert.Equal(1, rig.Tablet.Uploads);
        Assert.NotNull(rig.Delivered.Find(NameOf(Sam)));
    }
}

// The title a page is delivered under, for the tests of this file.
internal static class DeliveryNaming
{
    public static string NameOf(string person, int dayOffset) => DeliveredName.For(DeliveryKind.Workout, MondayDay(dayOffset), person);
}
