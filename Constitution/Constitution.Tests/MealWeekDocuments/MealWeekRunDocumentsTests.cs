using System.Text.RegularExpressions;
using Constitution.Domain.Delivery;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.MealWeekRun;
using static Constitution.Tests.Calendar.InventedFamily;
using Run = Constitution.Infrastructure.Meals.MealWeekRun;

namespace Constitution.Tests.MealWeekDocuments;

/// <summary>The documents step joined to the run: it follows the saved week, and only a run that planned one delivers anything.</summary>
public class MealWeekRunDocumentsTests
{
    private static (MealWeekRig Rig, DocsRig Docs) Rigs()
    {
        var rig = new MealWeekRig();
        var docs = new DocsRig();
        rig.Box.Of(DocsData.Cards().Concat(MealPlanning.MealData.Box()).ToList());
        return (rig, docs);
    }

    private static Task<MealWeekOutcome> RunAsync(MealWeekRig rig, DocsRig docs, MealWeekRequestInputs inputs) =>
        Run.RunAsync(inputs, rig.Seams with { Documents = docs.Seams }, CancellationToken.None);

    private static async Task<MealWeekReport> PlannedAsync(MealWeekRig rig, DocsRig docs) =>
        Assert.IsType<MealWeekOutcome.Planned>(await RunAsync(rig, docs, Inputs(rig))).Report;

    private static MealWeekRequestInputs Inputs(MealWeekRig rig) => rig.Inputs() with { DeliverFor = [Lee] };

    [Fact]
    public async Task APlannedWeekIsDeliveredAfterItIsKept()
    {
        var (rig, docs) = Rigs();

        var report = await PlannedAsync(rig, docs);

        var done = Assert.IsType<MealDocumentsResult.Done>(report.Documents);
        Assert.Equal(MealDocumentVerdict.Delivered, docs.Sheet(done).Verdict);
        Assert.Equal(1, rig.Records.Saves);
        Assert.Contains(docs.SheetTitle, docs.Tablet.Titles.Values);
        Assert.Single(docs.Expected.Recorded);
    }

    [Fact]
    public async Task TheSheetIsOfTheWeekThatWasKept()
    {
        var (rig, docs) = Rigs();

        var report = await PlannedAsync(rig, docs);

        var recorded = Assert.Single(docs.Expected.Recorded);
        Assert.Equal(report.Week.WeekStart, recorded.Day);
        Assert.Equal(report.Week.Dinners.Count, recorded.Pages[0].Rules.Count);
    }

    [Fact]
    public async Task AnUnchangedRunDeliversNothingNew()
    {
        var (rig, docs) = Rigs();
        await PlannedAsync(rig, docs);
        var uploads = docs.Tablet.Uploads;

        var again = await PlannedAsync(rig, docs);

        Assert.Equal(MealDocumentVerdict.Unchanged, docs.Sheet(Assert.IsType<MealDocumentsResult.Done>(again.Documents)).Verdict);
        Assert.Equal(uploads, docs.Tablet.Uploads);
        Assert.Equal(1, docs.Expected.Records);
    }

    [Fact]
    public async Task APhoneThatDoesNotPlanDeliversNothing()
    {
        var (rig, docs) = Rigs();
        rig.MayPlan = false;

        var outcome = await RunAsync(rig, docs, Inputs(rig));

        rig.StoppedWith(MealNotPlannedBecause.NotThePlanner, outcome);
        Assert.Empty(docs.Tablet.Calls);
        Assert.Empty(docs.Expected.Recorded);
    }

    [Fact]
    public async Task AWeekThatCouldNotBePlannedDeliversNothing()
    {
        var (rig, docs) = Rigs();
        rig.Box.Of(MealPlanning.MealData.Box().Take(5).ToList());

        var outcome = await RunAsync(rig, docs, Inputs(rig));

        rig.StoppedWith(MealNotPlannedBecause.BoxTooSmall, outcome);
        Assert.Empty(docs.Tablet.Calls);
    }

    [Fact]
    public async Task ARunWithoutDocumentSeamsPrintsNothing()
    {
        var rig = new MealWeekRig();

        var report = await rig.PlannedAsync(rig.Inputs() with { DeliverFor = [Lee] });

        Assert.Null(report.Documents);
    }

    [Fact]
    public async Task AStoppedDocumentsStepStillKeepsTheWeek()
    {
        var (rig, docs) = Rigs();
        docs.Roster.Answer = new MealRosterRead.Unavailable("The household file cannot be opened.");

        var report = await PlannedAsync(rig, docs);

        Assert.IsType<MealDocumentsResult.Stopped>(report.Documents);
        Assert.Equal(1, rig.Records.Saves);
        Assert.Contains(docs.Log.Phrases, phrase => phrase.StartsWith("meal documents not delivered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheInputsCarryWhoseTabletThisIs()
    {
        var setup = new Constitution.Domain.Calendar.PlanningSetup(
            [new Constitution.Domain.Calendar.HouseholdAdult(Sam, "sam@example.com"), new Constitution.Domain.Calendar.HouseholdAdult(Lee, "lee@example.com", DeliversHere: true)])
        {
            FamilyCalendarId = "family@example.com",
            NoChildren = true,
        };

        var prepared = MealWeekJoin.Prepare(new Constitution.Domain.Calendar.HouseholdSetupRead.Configured(setup), null, Zone, Edt(10, 21, 12));

        Assert.Equal([Lee], Assert.IsType<MealWeekPreparation.Ready>(prepared).Inputs.DeliverFor);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TheRunCallsTheStepOnlyAfterTheWeekIsSaved()
    {
        var text = File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => ConstitutionSource.Relative(file).EndsWith("Constitution.Infrastructure/Meals/MealWeekRun.cs", StringComparison.Ordinal)));

        Assert.True(text.IndexOf("seams.Records.Save(", StringComparison.Ordinal) < text.IndexOf("MealDocumentsStep.RunAsync(", StringComparison.Ordinal));
        await Task.CompletedTask;
    }
}
