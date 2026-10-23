using Constitution.Domain.Calendar;
using Constitution.Domain.Ink;
using Constitution.Domain.Delivery;
using Constitution.Domain.Equipment;
using Constitution.Domain.Notices;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Tests.Documents;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The paper half of the run: which pages go to the tablet and when, what each says, and C-2.3.6 on the real run: a page
/// is replaced only before its slot, never once the session has begun, always under the one title.
/// </summary>
public sealed class WorkoutWeekDeliveryTests : IDisposable
{
    private readonly WeekRig rig = new();

    public void Dispose() => rig.Dispose();

    private static readonly Session Cardio = new(SessionKind.OutdoorCardio, TimeSpan.FromMinutes(35), true, [new("Warm-up walk"), new("Brisk walk"), new("Cool-down walk")]);

    private static string NameOf(string person, int dayOffset = 0) => DeliveredName.For(DeliveryKind.Workout, Day(dayOffset), person);

    private static string TextOf(byte[] pdf) =>
        string.Join(" ", PdfReadBack.Open(pdf).GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));

    /// <summary>Sam has one outdoor cardio session, Monday at 4 pm; Lee has none.</summary>
    private void SamWalksMonday()
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Cardio)));
    }

    // ---- which pages ----

    [Fact]
    public async Task OnlyTodaysPageGoesOutByDefault()
    {
        rig.BothAreEstablished();

        var report = await rig.PlannedAsync();

        var today = report.Plan.Plans.SelectMany(week => week.Sessions).Count(session => session.Day == Day(0));
        Assert.True(today >= 1, "The invented week should have a session on Monday.");
        Assert.Equal(today, rig.Tablet.Uploads);
        Assert.All(rig.UploadedTitles, title => Assert.Contains("2026-11-02", title, StringComparison.Ordinal));
    }

    [Fact]
    public async Task APageAheadGoesOutWhenAskedFor()
    {
        rig.BothAreEstablished();

        var report = await rig.PlannedAsync(rig.Request() with { DeliverAheadDays = 1 });

        var wanted = report.Plan.Plans.SelectMany(week => week.Sessions).Count(session => session.Day <= Day(1));
        Assert.Equal(wanted, rig.Tablet.Uploads);
        Assert.Contains(rig.UploadedTitles, title => title.Contains("2026-11-03", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.UploadedTitles, title => title.Contains("2026-11-04", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PagesGoToTheRootUnderTheirName()
    {
        SamWalksMonday();

        var report = await rig.PlannedAsync();

        var page = Assert.Single(report.Documents);
        Assert.Equal(NameOf(Sam), page.Name);
        Assert.Equal(DocumentVerdict.Delivered, page.Verdict);
        Assert.Equal([NameOf(Sam)], rig.UploadedTitles);
    }

    [Fact]
    public async Task ADayThatIsOverGetsNoPageOnALateFirstRun()
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(
            rig.Session(Sam, 0, 16, Cardio),
            rig.Session(Sam, 1, 16, new Session(SessionKind.Mobility, TimeSpan.FromMinutes(20), false, [new Exercise("Cat and cow", EquipmentCatalogue.MatId)]))));

        var report = await rig.PlannedAsync(rig.Request(Est(11, 3, 5, 30)));

        Assert.Equal([Day(1)], report.Documents.Select(item => item.Day));
        Assert.Equal([NameOf(Sam, 1)], rig.UploadedTitles);
    }

    [Fact]
    public async Task OnlyTheNamedPeoplesPagesGoToThisTablet()
    {
        rig.BothAreEstablished();
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, Cardio), rig.Session(Lee, 0, 16, Cardio)));

        var report = await rig.PlannedAsync(rig.Request(null, Lee));

        Assert.Equal([NameOf(Lee)], rig.UploadedTitles);
        Assert.Equal(2, report.Placement.Created.Count);
    }

    [Fact]
    public async Task ASessionOffTheCalendarGetsNoPageAtAll()
    {
        SamWalksMonday();
        rig.Writer.FailingPeople.Add(Sam);

        var report = await rig.PlannedAsync();

        var page = Assert.Single(report.Documents);
        Assert.Equal(DocumentVerdict.NotOnCalendar, page.Verdict);
        Assert.Empty(rig.Tablet.Calls);
        Assert.Empty(rig.Expected.Outstanding());
    }

    // ---- what is recorded, and when ----

    [Fact]
    public async Task ADeliveredPageIsRecordedForReadBack()
    {
        SamWalksMonday();

        await rig.PlannedAsync();

        var waiting = Assert.Single(rig.Expected.Outstanding());
        Assert.Equal(NameOf(Sam), waiting.Document.Name);
        Assert.Equal(SessionKind.OutdoorCardio, waiting.Document.Session);
        Assert.False(waiting.Saved);
        var delivered = rig.Delivered.Find(NameOf(Sam))!;
        Assert.Equal(At(0, 16), delivered.SlotStart);
        Assert.Equal(64, delivered.Fingerprint.Length);
    }

    [Fact]
    public async Task NothingIsRecordedWhenTheTabletRefuses()
    {
        SamWalksMonday();
        rig.Tablet.UploadFails = new HttpRequestException("The cloud said no.");

        var report = await rig.PlannedAsync();

        var page = Assert.Single(report.Documents);
        Assert.Equal(DocumentVerdict.NotDelivered, page.Verdict);
        Assert.IsType<HttpRequestException>(page.Failure);
        Assert.Empty(rig.Expected.Outstanding());
        Assert.Null(rig.Delivered.Find(NameOf(Sam)));
        Assert.Equal(NoticeKind.ReMarkableDelivery, Assert.Single(rig.Access.Board.Waiting()).Kind);
    }

    [Fact]
    public async Task ARefusedPageIsSentOnTheNextRun()
    {
        SamWalksMonday();
        rig.Tablet.UploadFails = new HttpRequestException("The cloud said no.");
        await rig.PlannedAsync();
        rig.Tablet.UploadFails = null;

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 6)));

        Assert.Equal(DocumentVerdict.Delivered, Assert.Single(report.Documents).Verdict);
        Assert.Single(rig.Expected.Outstanding());
    }

    // ---- C-2.3.6 on the real run ----

    [Fact]
    public async Task AFlippedForecastReplacesThePageBeforeTheSlot()
    {
        SamWalksMonday();
        await rig.PlannedAsync();
        var first = rig.Tablet.PdfOf(NameOf(Sam));
        rig.Conditions.Read = ConditionsRead.Of(WorkoutWeekRunTests.RainAcross(0, 16, 18));

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 12)));

        var page = Assert.Single(report.Documents);
        Assert.Equal(DocumentVerdict.Replaced, page.Verdict);
        Assert.Equal([NameOf(Sam)], rig.Tablet.Titles.Values);
        Assert.NotEqual(first, rig.Tablet.PdfOf(NameOf(Sam)));
        Assert.Contains("Treadmill", TextOf(rig.Tablet.PdfOf(NameOf(Sam))), StringComparison.Ordinal);
        Assert.Single(rig.Expected.Outstanding());
        Assert.Equal(SessionKind.Treadmill, rig.Delivered.Find(NameOf(Sam))!.Kind);
    }

    [Fact]
    public async Task ForecastAfterTheSlotChangesNothing()
    {
        SamWalksMonday();
        await rig.PlannedAsync();
        var uploads = rig.Tablet.Uploads;
        var first = rig.Tablet.PdfOf(NameOf(Sam));
        rig.Conditions.Read = ConditionsRead.Of(WorkoutWeekRunTests.RainAcross(0, 16, 18));

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 16, 20)));

        // The adapters leave a session that has begun exactly as it was, so there is nothing to send.
        Assert.Equal(DocumentVerdict.Unchanged, Assert.Single(report.Documents).Verdict);
        Assert.Equal(uploads, rig.Tablet.Uploads);
        Assert.Equal(first, rig.Tablet.PdfOf(NameOf(Sam)));
    }

    [Fact]
    public async Task ANewNoteAfterTheSlotBeganSendsNothing()
    {
        SamWalksMonday();
        await rig.PlannedAsync();
        var uploads = rig.Tablet.Uploads;
        var first = rig.Tablet.PdfOf(NameOf(Sam));
        rig.Learning.Held = rig.Learning.Held with { Soreness = SorenessLedger.Empty.Note(Sam, "my left shoulder", new DateOnly(2026, 11, 2)) };

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 16, 20)));

        Assert.Equal(DocumentVerdict.TooLate, Assert.Single(report.Documents).Verdict);
        Assert.Equal(uploads, rig.Tablet.Uploads);
        Assert.DoesNotContain(rig.Tablet.Calls, call => call.StartsWith("trash:", StringComparison.Ordinal));
        Assert.Equal(first, rig.Tablet.PdfOf(NameOf(Sam)));
    }

    [Fact]
    public async Task ThatSameNoteBeforeTheSlotReplacesThePage()
    {
        SamWalksMonday();
        await rig.PlannedAsync();
        rig.Learning.Held = rig.Learning.Held with { Soreness = SorenessLedger.Empty.Note(Sam, "my left shoulder", new DateOnly(2026, 11, 2)) };

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 15, 59)));

        Assert.Equal(DocumentVerdict.Replaced, Assert.Single(report.Documents).Verdict);
        Assert.Contains("Still sore?", TextOf(rig.Tablet.PdfOf(NameOf(Sam))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARainedOutSessionBeforeItsSlotTakesItsPageOff()
    {
        rig.BothAreEstablished();
        var sunday = new Session(SessionKind.GentleWalk, TimeSpan.FromMinutes(30), false, [new Exercise("Easy walk")]);
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 6, 14, sunday)));
        var request = rig.Request(Est(11, 8, 6)) with { Equipment = EquipmentSet.Of([]) };
        await rig.PlannedAsync(request);
        Assert.Single(rig.Tablet.Titles);
        rig.Conditions.Read = ConditionsRead.Of(WorkoutWeekRunTests.RainAcross(6, 6, 18));

        var report = await rig.PlannedAsync(request with { Now = Est(11, 8, 10) });

        Assert.Equal(DocumentVerdict.Withdrawn, Assert.Single(report.Documents).Verdict);
        Assert.Empty(rig.Tablet.Titles);
        Assert.Empty(rig.Expected.Outstanding());
        Assert.Null(rig.Delivered.Find(NameOf(Sam, 6)));
    }

    [Fact]
    public async Task ADeletedEventTakesItsPageOff()
    {
        SamWalksMonday();
        var first = await rig.PlannedAsync();
        var key = new PlacementIndex(first.PlacedPlan).KeyFor(Sam, Day(0))!.Value;
        rig.Writer.OwnerDeletes(key, Sam);

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 9)));

        Assert.Equal(DocumentVerdict.Withdrawn, Assert.Single(report.Documents).Verdict);
        Assert.Empty(rig.Tablet.Titles);
        Assert.Empty(rig.Expected.Outstanding());
    }

    [Fact]
    public async Task AnUnreachableCalendarDoesNotPullAPageOff()
    {
        SamWalksMonday();
        await rig.PlannedAsync();
        rig.Writer.UnavailablePeople.Add(Sam);

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 9)));

        // Not knowing is not a delete: Google not answering is no reason to empty the tablet.
        Assert.Equal(DocumentVerdict.NotOnCalendar, Assert.Single(report.Documents).Verdict);
        Assert.Equal([NameOf(Sam)], rig.Tablet.Titles.Values);
        Assert.NotNull(rig.Delivered.Find(NameOf(Sam)));
        Assert.Single(rig.Expected.Outstanding());
        Assert.DoesNotContain(rig.Tablet.Calls, call => call.StartsWith("trash:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUnreadableRecordPullsNoPageOff()
    {
        SamWalksMonday();
        await rig.PlannedAsync();
        rig.Keys.Json = "{ not the record";

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 9)));

        Assert.Null(report.Placement.Plan);
        Assert.Equal(DocumentVerdict.NotOnCalendar, Assert.Single(report.Documents).Verdict);
        Assert.Equal([NameOf(Sam)], rig.Tablet.Titles.Values);
    }

    [Fact]
    public async Task AnEventTheOwnerMovedKeepsItsOwnTimeOnThePage()
    {
        SamWalksMonday();
        var first = await rig.PlannedAsync();
        var key = new PlacementIndex(first.PlacedPlan).KeyFor(Sam, Day(0))!.Value;
        rig.Writer.OwnerMoves(key, Sam, At(0, 17), At(0, 17, 35));

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 9)));

        Assert.Equal(DocumentVerdict.Replaced, Assert.Single(report.Documents).Verdict);
        Assert.Equal(At(0, 17), rig.Delivered.Find(NameOf(Sam))!.SlotStart);
        Assert.Contains("5:00", TextOf(rig.Tablet.PdfOf(NameOf(Sam))), StringComparison.Ordinal);
    }

    // ---- what a page says ----

    [Fact]
    public async Task APageThatCannotBeDrawnDoesNotStopTheNext()
    {
        rig.BothAreEstablished();
        var crowded = new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, Enumerable.Range(1, 30).Select(number => new Exercise($"Move number {number}")).ToList());
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, crowded), rig.Session(Lee, 0, 16, Cardio)));

        var report = await rig.PlannedAsync();

        var built = report.Documents.Single(item => item.Person == Sam);
        Assert.Equal(DocumentVerdict.NotBuilt, built.Verdict);
        Assert.IsType<ArgumentException>(built.Failure);
        Assert.Equal(DocumentVerdict.Delivered, report.Documents.Single(item => item.Person == Lee).Verdict);
        Assert.Equal([NameOf(Lee)], rig.UploadedTitles);
    }

    [Fact]
    public async Task AFreshInstallsPagesAreTheAssessmentPages()
    {
        var report = await rig.PlannedAsync();

        Assert.NotEmpty(report.Documents);
        Assert.All(report.Documents, item => Assert.Equal(DocumentVerdict.Delivered, item.Verdict));
        foreach (var title in rig.Tablet.Titles)
        {
            var text = TextOf(rig.Tablet.Pdfs[title.Key]);
            Assert.Contains("Where you start", text, StringComparison.Ordinal);
            Assert.Contains("no score", text, StringComparison.Ordinal);
        }

        var waiting = rig.Expected.Outstanding();
        Assert.Equal(report.Documents.Count, waiting.Count);
        Assert.All(waiting, item => Assert.Contains(item.Document.Pages.SelectMany(page => page.Rules), rule => rule is NumberRule or RatingRule));
    }

    [Fact]
    public async Task AStrengthPageCarriesTheLaddersDoseAndPounds()
    {
        rig.BothAreEstablished();
        var lift = new Session(
            SessionKind.Strength, TimeSpan.FromMinutes(40), false,
            [new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId), new Exercise("Wall angels")]);
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, lift)));

        await rig.PlannedAsync();

        var text = TextOf(rig.Tablet.PdfOf(NameOf(Sam)));
        Assert.Contains("2 × 8", text, StringComparison.Ordinal);
        Assert.Contains("10 lb dumbbell", text, StringComparison.Ordinal);
        Assert.Contains("2 × 6", text, StringComparison.Ordinal);
        Assert.Contains("Score", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALiftThatWentUpAsksTheEncouragementSeatOnce()
    {
        rig.BothAreEstablished();
        var exercise = new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId);
        var lift = new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [exercise]);
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, lift)));
        var pages = new SessionPages(Sam, new DateOnly(2026, 10, 28), SessionKind.Strength, [new ExercisePageRef(exercise, "done", "feel")]);
        var ledger = ProgressionLedger.Empty;
        foreach (var _ in Enumerable.Range(0, 2))
        {
            ledger = ledger.Settle(pages, [new InkResolution.PageDone("done", "easy")]);
        }

        rig.Learning.Held = rig.Learning.Held with { Progression = ledger };
        rig.Encouragement.Answer = "Five pounds more than last time.";

        await rig.PlannedAsync();

        var news = Assert.Single(rig.Encouragement.Asked);
        Assert.Equal(15, news.LoadToday);
        Assert.Equal(10, news.LoadLast);
        Assert.Contains("Five pounds more", TextOf(rig.Tablet.PdfOf(NameOf(Sam))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePageIsSilentWhenThereIsNoNews()
    {
        SamWalksMonday();

        await rig.PlannedAsync();

        Assert.Empty(rig.Encouragement.Asked);
    }

    [Fact]
    public async Task AStrengthPageWithNoNewsAsksNoOneEither()
    {
        rig.BothAreEstablished();
        var lift = new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId), new Exercise("Wall angels")]);
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, lift)));

        await rig.PlannedAsync();

        Assert.Empty(rig.Encouragement.Asked);
        Assert.DoesNotContain(rig.Encouragement.Answer!, TextOf(rig.Tablet.PdfOf(NameOf(Sam))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInjuryDayPageAsksStillSoreAndNamesTheStandIn()
    {
        rig.BothAreEstablished();
        var lift = new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [new Exercise("Step-ups onto a bench", EquipmentCatalogue.BenchId), new Exercise("Wall angels")]);
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, lift)));
        rig.Learning.Held = rig.Learning.Held with { Soreness = SorenessLedger.Empty.Note(Sam, "my left knee", new DateOnly(2026, 10, 30)) };

        await rig.PlannedAsync();

        var text = TextOf(rig.Tablet.PdfOf(NameOf(Sam)));
        Assert.Contains("Still sore?", text, StringComparison.Ordinal);
        Assert.Contains("knee", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("In place of", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDoctorNotePrintsOnceAndSurvivesAReplacement()
    {
        rig.BothAreEstablished();
        var lift = new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [new Exercise("Step-ups onto a bench", EquipmentCatalogue.BenchId), new Exercise("Wall angels")]);
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, lift)));
        rig.Learning.Held = rig.Learning.Held with { Soreness = SorenessLedger.Empty.Note(Sam, "my left knee", new DateOnly(2026, 10, 20)) };

        await rig.PlannedAsync();

        Assert.Equal([Sam], rig.Learning.DoctorNotes);
        Assert.Contains("doctor", TextOf(rig.Tablet.PdfOf(NameOf(Sam))), StringComparison.Ordinal);

        // The soreness is now marked printed, so a fresh page would drop the note; the replacement must keep it.
        rig.Recovery.Reads[Sam] = new RecoveryRead(RecoveryState.UnderRecovered, [RecoveryReason.ShortSleep], [], []);
        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 9)));

        Assert.Equal(DocumentVerdict.Replaced, Assert.Single(report.Documents).Verdict);
        Assert.Contains("doctor", TextOf(rig.Tablet.PdfOf(NameOf(Sam))), StringComparison.Ordinal);
        Assert.Equal([Sam], rig.Learning.DoctorNotes);
    }

    [Fact]
    public async Task AVoicedLineArrivingLaterIsNotAReasonToResend()
    {
        rig.BothAreEstablished();
        var exercise = new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId);
        var lift = new Session(SessionKind.Strength, TimeSpan.FromMinutes(40), false, [exercise]);
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(rig.Session(Sam, 0, 16, lift)));
        var pages = new SessionPages(Sam, new DateOnly(2026, 10, 28), SessionKind.Strength, [new ExercisePageRef(exercise, "done", "feel")]);
        var ledger = ProgressionLedger.Empty;
        foreach (var _ in Enumerable.Range(0, 2))
        {
            ledger = ledger.Settle(pages, [new InkResolution.PageDone("done", "easy")]);
        }

        rig.Learning.Held = rig.Learning.Held with { Progression = ledger };
        rig.Encouragement.Answer = "15 lb today, up from 10.";
        await rig.PlannedAsync();
        rig.Encouragement.Answer = "A better line, in the owner's own hand.";

        var report = await rig.PlannedAsync(rig.Request(Est(11, 2, 9)));

        Assert.Equal(DocumentVerdict.Unchanged, Assert.Single(report.Documents).Verdict);
        Assert.Equal(1, rig.Tablet.Uploads);
    }
}
