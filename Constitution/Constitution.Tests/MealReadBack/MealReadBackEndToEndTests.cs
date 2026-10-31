using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.Meals;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.MealSheet;
using Constitution.Tests.ReadBack;
using static Constitution.Tests.MealReadBack.MealRig;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.MealReadBack;

/// <summary>
/// A recorded document (through the real expected-documents file and its codec), a synthetic page, the real read-back service with a fake
/// tablet and a fake reader, and the real household file service: the answers appear in the household file, or the page stays.
/// </summary>
public class MealReadBackEndToEndTests : IDisposable
{
    private static readonly DateTimeOffset BeforeDeadline = new(2026, 11, 21, 7, 15, 0, TimeSpan.FromHours(-5));
    private static readonly DateTimeOffset AfterDeadline = new(2026, 11, 22, 8, 30, 0, TimeSpan.FromHours(-5));

    private readonly string folder = Path.Combine(Path.GetTempPath(), "meal-readback-" + Guid.NewGuid().ToString("N"));
    private readonly MealRig household = new();
    private readonly Rig rig = new(proven: true);

    public MealReadBackEndToEndTests() => Directory.CreateDirectory(folder);

    private FileExpectedDocuments Expected => new(Path.Combine(folder, "expected.json"));

    private ReadBackService Service(FileExpectedDocuments expected)
    {
        var learning = new FileWorkoutLearning(rig.Ledger, Path.Combine(folder, "learning"));
        var hooks = new SettledHooks(learning, household.Hook);
        return new ReadBackService(expected, rig.Tablet, rig.Vision, rig.Results, rig.Ledger, rig.Proof, rig.Notices, rig.Log, () => rig.Now, hooks);
    }

    private ExpectedDocument Recorded(FileExpectedDocuments expected, ExpectedDocument document)
    {
        expected.Record(document);
        var back = expected.Outstanding().Single(item => item.Document.Name == document.Name).Document;
        rig.Tablet.Has(back);
        return back;
    }

    /// <summary>The page as the reader would report it: the named boxes marked, the named boxes carrying words, every other box empty.</summary>
    private static Func<ExpectedDocument, InkDocumentRead> Page(IReadOnlySet<string> marked, IReadOnlyDictionary<string, string> words) => document =>
        new InkDocumentRead(
            document.Pages.ToDictionary(
                page => page.Number,
                page => InkPageRead.Read(page.AskedTargets.Select(target => words.TryGetValue(target.Id, out var text)
                    ? new InkTargetRead(target.Id, true, Sure, text)
                    : new InkTargetRead(target.Id, marked.Contains(target.Id), Sure)))),
            PenStrokes.Seen);

    private static readonly IReadOnlyDictionary<string, string> TuesdayDenied = new Dictionary<string, string>
    {
        [MealSheetIds.DinnerReason(DayOfWeek.Tuesday)] = "too heavy for a school night",
    };

    private static readonly IReadOnlySet<string> MondayTicked = new HashSet<string> { MealSheetIds.DinnerYes(DayOfWeek.Monday) };

    public void Dispose()
    {
        household.Dispose();
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ASheetReadAfterItsDeadlineLandsItsAnswers()
    {
        var expected = Expected;
        Recorded(expected, Sheet());
        rig.Now = AfterDeadline;
        rig.Vision.Reply = Page(MondayTicked, TuesdayDenied);

        var report = await Service(expected).Run();

        Assert.IsType<DocumentReadBack.Settled>(Assert.Single(report.Documents));
        var answers = household.Answers();
        Assert.Equal(7, answers.Count);
        Assert.All(answers, answer => Assert.Equal("sam", answer.Person.Value));
        Assert.Equal(MealVerdict.Approved, answers.Single(a => a.ItemId == Yes(DayOfWeek.Monday)).Verdict);
        Assert.Equal("too heavy for a school night", answers.Single(a => a.ItemId == Yes(DayOfWeek.Tuesday)).Reason);
        Assert.Equal(5, answers.Count(a => a.Verdict == MealVerdict.Unchanged));
        Assert.Empty(expected.Outstanding());
        Assert.Single(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task ASheetKeptForItsDeadlineStillRecordsOnTimeAnswers()
    {
        var expected = Expected;
        Recorded(expected, Sheet());
        rig.Now = BeforeDeadline;
        rig.Vision.Reply = Page(MondayTicked, TuesdayDenied);

        var report = await Service(expected).Run();

        Assert.IsType<DocumentReadBack.Unfinished>(Assert.Single(report.Documents));
        Assert.Single(expected.Outstanding());
        Assert.Empty(rig.Tablet.Trashed);
        var tuesday = household.Answers().Single(a => a.ItemId == Yes(DayOfWeek.Tuesday));
        Assert.Equal(MealVerdict.Denied, tuesday.Verdict);
        Assert.Equal(BeforeDeadline.ToUnixTimeMilliseconds(), tuesday.ReadAt.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task TheOnTimeReadingStandsWhenThePageIsFinallySettled()
    {
        var expected = Expected;
        Recorded(expected, Sheet());
        var service = Service(expected);
        rig.Now = BeforeDeadline;
        rig.Vision.Reply = Page(MondayTicked, TuesdayDenied);
        await service.Run();

        rig.Now = AfterDeadline;
        var report = await service.Run();

        Assert.IsType<DocumentReadBack.Settled>(Assert.Single(report.Documents));
        var tuesday = household.Answers().Single(a => a.ItemId == Yes(DayOfWeek.Tuesday));
        Assert.Equal(BeforeDeadline.ToUnixTimeMilliseconds(), tuesday.ReadAt.ToUnixTimeMilliseconds());
        Assert.Equal(7, household.Answers().Count);
    }

    [Fact]
    public async Task AFailedWriteKeepsThePageAndMarksNothingSaved()
    {
        var expected = Expected;
        Recorded(expected, Sheet());
        household.MakeUnreadable();
        rig.Now = AfterDeadline;
        rig.Vision.Reply = Page(MondayTicked, TuesdayDenied);

        var report = await Service(expected).Run();

        var failed = Assert.IsType<DocumentReadBack.Failed>(Assert.Single(report.Documents));
        Assert.Equal(ReadBackStage.Persist, failed.Stage);
        Assert.False(Assert.Single(expected.Outstanding()).Saved);
        Assert.Empty(rig.Tablet.Trashed);
        Assert.DoesNotContain("MarkSaved", rig.Calls.Calls);
    }

    [Fact]
    public async Task ThePartnersTabletIsTheirOwnAnswers()
    {
        using var partner = new MealRig(Storyworthtelling.Household.HouseholdFileRole.SharedWith);
        var expected = Expected;
        Recorded(expected, Sheet("Lee"));
        rig.Now = AfterDeadline;
        rig.Vision.Reply = Page(MondayTicked, TuesdayDenied);
        var hooks = new SettledHooks(partner.Hook);
        var service = new ReadBackService(expected, rig.Tablet, rig.Vision, rig.Results, rig.Ledger, rig.Proof, rig.Notices, rig.Log, () => rig.Now, hooks);

        await service.Run();

        Assert.All(partner.Answers(), answer => Assert.Equal("lee", answer.Person.Value));
        Assert.Equal(7, partner.Answers().Count);
    }

    [Fact]
    public async Task ARecipePageLandsTheRatingsTheCookCircled()
    {
        var expected = Expected;
        Recorded(expected, Recipe());
        rig.Now = NextMorning;
        rig.Vision.Reply = Page(new HashSet<string> { "rate-1-2", "rate-3-3", "rate-4-1" }, new Dictionary<string, string>());

        var report = await Service(expected).Run();

        Assert.IsType<DocumentReadBack.Settled>(Assert.Single(report.Documents));
        var ratings = household.Ratings().OrderBy(rating => rating.Person.Value, StringComparer.Ordinal).ToList();
        Assert.Equal(["juniper", "robin", "sam"], ratings.Select(rating => rating.Person.Value));
        Assert.Equal([TasteVerdict.Loved, TasteVerdict.NeverAgain, TasteVerdict.Fine], ratings.Select(rating => rating.Verdict));
        Assert.All(ratings, rating => Assert.Equal(DinnerDay, rating.Day));
        Assert.Empty(expected.Outstanding());
    }

    [Fact]
    public async Task WeDidNotCookTonightRecordsNoRatings()
    {
        var expected = Expected;
        Recorded(expected, Recipe());
        rig.Now = NextMorning;
        rig.Vision.Reply = Page(new HashSet<string> { "rate-1-2", RecipeIds.DidntCook }, new Dictionary<string, string>());

        var report = await Service(expected).Run();

        Assert.IsType<DocumentReadBack.Settled>(Assert.Single(report.Documents));
        Assert.Empty(household.Ratings());
    }

    [Fact]
    public async Task AWorkoutThroughTheSameServiceStillTeachesTheLadders()
    {
        var expected = Expected;
        Recorded(expected, Workout());
        rig.Now = Morning;
        rig.Vision.Reply = document => Read(document, PenStrokes.Seen, (2, "done"));
        var before = household.Document();

        var report = await Service(expected).Run();

        Assert.IsType<DocumentReadBack.Settled>(Assert.Single(report.Documents));
        Assert.True(before.ContentEquals(household.Document()));
        Assert.True(File.Exists(Path.Combine(folder, "learning", "progression.json")));
    }
}
