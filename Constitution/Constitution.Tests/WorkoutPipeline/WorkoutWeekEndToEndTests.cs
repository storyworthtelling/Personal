using Constitution.Domain.Delivery;
using Constitution.Domain.Equipment;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.ReadBack;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;
using ReadBackFakeLedger = Constitution.Tests.ReadBack.FakeLedger;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The whole thing on a known week, with the real read-back at the other end. Sam strengthens on Monday and Friday and
/// does mobility on Wednesday; Lee walks on Monday and Thursday. Each morning the run delivers that day's page; the next
/// morning the real read-back looks for it by title on a stand-in for the cloud, reads it against the boxes the run
/// recorded, and trashes it.
/// </summary>
public sealed class WorkoutWeekEndToEndTests : IDisposable
{
    private readonly WeekRig rig = new();

    public WorkoutWeekEndToEndTests()
    {
        rig.BothAreEstablished();
        var lift = new Session(
            SessionKind.Strength, TimeSpan.FromMinutes(40), false,
            [new Exercise("Squat holding a weight at your chest", EquipmentCatalogue.DumbbellsId), new Exercise("Band row", EquipmentCatalogue.BandsId), new Exercise("Wall angels")]);
        var mobility = new Session(SessionKind.Mobility, TimeSpan.FromMinutes(20), false, [new("Cat and cow", EquipmentCatalogue.MatId), new("Child's pose", EquipmentCatalogue.MatId)]);
        var walk = new Session(SessionKind.OutdoorCardio, TimeSpan.FromMinutes(35), true, [new("Warm-up walk"), new("Brisk walk"), new("Cool-down walk")]);
        rig.AuthorWith(_ => WeekRig.PlanOfSessions(
            rig.Session(Sam, 0, 16, lift), rig.Session(Sam, 2, 16, mobility), rig.Session(Sam, 4, 16, lift),
            rig.Session(Lee, 0, 16, walk), rig.Session(Lee, 3, 16, walk)));
    }

    public void Dispose() => rig.Dispose();

    private static string NameOf(string person, int dayOffset) => DeliveredName.For(DeliveryKind.Workout, Day(dayOffset), person);

    private (ReadBackService Service, CallLog Calls, ReadBackFakeLedger Ledger, FakeVision Vision, FakeLog Log) ReadBack(DateTimeOffset morning, bool onlyDueDocuments, bool proven = true)
    {
        var calls = new CallLog();
        var ledger = new ReadBackFakeLedger(calls);
        var vision = new FakeVision(calls) { Reply = document => ReadBackFixture.Read(document, PenStrokes.Seen) };
        var log = new FakeLog();
        IExpectedDocuments store = onlyDueDocuments ? new DueExpectedDocuments(rig.Expected, () => morning) : rig.Expected;
        var service = new ReadBackService(
            store, new RemarkableAnnotatedPageSource(rig.Tablet), vision, new FakeResults(calls), ledger, new FakeProof(calls, proven),
            new FakeBoard(calls), log, () => morning);
        return (service, calls, ledger, vision, log);
    }

    [Fact]
    public async Task EachMorningDeliversThatDaysPages()
    {
        var delivered = new List<string>();
        foreach (var day in new[] { 0, 1, 2, 3, 4 })
        {
            var report = await rig.PlannedAsync(rig.Request(Est(11, 2 + day, 5, 30)));
            delivered.AddRange(report.Documents.Where(item => item.Verdict == DocumentVerdict.Delivered).Select(item => item.Name));
        }

        string[] expected = [NameOf(Sam, 0), NameOf(Lee, 0), NameOf(Sam, 2), NameOf(Lee, 3), NameOf(Sam, 4)];
        Assert.Equal(expected.Order(StringComparer.Ordinal), delivered.Order(StringComparer.Ordinal));
        Assert.Equal(delivered.Count, rig.Tablet.Titles.Count);
    }

    [Fact]
    public async Task ReadBackFindsTheRecordedTitles()
    {
        await rig.PlannedAsync(rig.Request(Est(11, 2, 5, 30)));
        var names = rig.UploadedTitles.Order(StringComparer.Ordinal).ToList();
        Assert.Equal([NameOf(Lee, 0), NameOf(Sam, 0)], names);
        Assert.Equal(names, rig.Expected.Outstanding().Select(item => item.Document.Name).Order(StringComparer.Ordinal).ToList());

        var (service, _, _, _, log) = ReadBack(Est(11, 3, 7, 15), onlyDueDocuments: true);
        var report = await service.Run();

        Assert.Equal(2, report.Documents.Count);
        Assert.All(report.Documents, outcome => Assert.IsType<DocumentReadBack.Settled>(outcome));
        var looked = rig.Tablet.Calls.Where(call => call.StartsWith("download:", StringComparison.Ordinal)).Select(call => call["download:".Length..]);
        Assert.Equal(names, looked.Order(StringComparer.Ordinal).ToList());
        Assert.Empty(rig.Tablet.Titles);
        Assert.Empty(rig.Expected.Outstanding());
        Assert.All(log.Phrases, phrase => Assert.StartsWith("ink read back settled", phrase, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATickOnTheExercisePageClosesTheDayAsDone()
    {
        await rig.PlannedAsync(rig.Request(Est(11, 2, 5, 30)));
        var (service, _, ledger, vision, _) = ReadBack(Est(11, 3, 7, 15), onlyDueDocuments: true);
        vision.Reply = document => document.Person == Sam
            ? ReadBackFixture.Read(document, PenStrokes.Seen, (2, Constitution.Domain.Documents.WorkoutIds.Done))
            : ReadBackFixture.Read(document, PenStrokes.Seen);

        await service.Run();

        Assert.Equal(SessionOutcome.Done, ledger.Held.OutcomeOf(Sam, Day(0)));
        Assert.Equal(SessionOutcome.Skipped, ledger.Held.OutcomeOf(Lee, Day(0)));
    }

    [Fact]
    public async Task WithoutTheFilterTodaysPageReadsAsASkip()
    {
        // The hazard the DueExpectedDocuments view exists for: the read-back reads whatever it is told about, whatever its day.
        await rig.PlannedAsync(rig.Request(Est(11, 2, 5, 30)));
        var (service, _, ledger, _, _) = ReadBack(Est(11, 2, 7, 15), onlyDueDocuments: false);

        await service.Run();

        Assert.Equal(SessionOutcome.Skipped, ledger.Held.OutcomeOf(Sam, Day(0)));
    }

    [Fact]
    public async Task WithTheDueFilterTodaysPageIsLeftAlone()
    {
        await rig.PlannedAsync(rig.Request(Est(11, 2, 5, 30)));
        var (service, _, ledger, vision, _) = ReadBack(Est(11, 2, 7, 15), onlyDueDocuments: true);

        var report = await service.Run();

        Assert.Empty(report.Documents);
        Assert.Equal(0, vision.Reads);
        Assert.Null(ledger.Held.OutcomeOf(Sam, Day(0)));
        Assert.Equal(2, rig.Tablet.Titles.Count);
    }

    [Fact]
    public async Task AWholeWeekOfMorningsThenAgainChangesNothing()
    {
        var mornings = Enumerable.Range(0, 7).Select(day => Est(11, 2 + day, 5, 30)).ToList();
        foreach (var morning in mornings)
        {
            await rig.PlannedAsync(rig.Request(morning));
        }

        var calendar = rig.Writer.Snapshot();
        var titles = rig.Tablet.Titles.Values.Order().ToList();
        var uploads = rig.Tablet.Uploads;
        var expected = rig.Expected.Outstanding().Select(item => item.Document.Name).Order().ToList();
        var creates = rig.Writer.CreateCalls;

        foreach (var morning in mornings)
        {
            var report = await rig.PlannedAsync(rig.Request(morning.AddMinutes(30)));
            Assert.All(report.Documents, item => Assert.True(item.Verdict is DocumentVerdict.Unchanged or DocumentVerdict.TooLate, $"{item.Verdict}"));
        }

        Assert.Equal(calendar, rig.Writer.Snapshot());
        Assert.Equal(creates, rig.Writer.CreateCalls);
        Assert.Equal(uploads, rig.Tablet.Uploads);
        Assert.Equal(titles, rig.Tablet.Titles.Values.Order().ToList());
        Assert.Equal(expected, rig.Expected.Outstanding().Select(item => item.Document.Name).Order().ToList());
        Assert.Equal(5, rig.Writer.All.Count);
    }
}
