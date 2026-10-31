using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.MealWeekDocuments;
using Constitution.Tests.MealWeekRun;
using Constitution.Tests.Templates;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.TemplatesInTheRun.TemplateRunData;
using Run = Constitution.Infrastructure.Meals.MealWeekRun;

namespace Constitution.Tests.TemplatesInTheRun;

/// <summary>The templates step joined into the weekly meal run: after the week is kept, before the paper, and only on the phone that plans.</summary>
public class MealWeekRunTemplatesTests
{
    private sealed record Rigs(MealWeekRig Rig, DocsRig Docs, FakeTemplateStore Store, FakeTemplateAuthor Author)
    {
        public MealWeekSeams Seams => Rig.Seams with { Documents = Docs.Seams, Templates = new MealTemplateSeams(Store, Author) };

        public MealWeekRequestInputs Inputs => Rig.Inputs() with { DeliverFor = [Lee] };

        public async Task<MealWeekReport> PlannedAsync() =>
            Assert.IsType<MealWeekOutcome.Planned>(await Run.RunAsync(Inputs, Seams, CancellationToken.None)).Report;
    }

    private static Rigs Make(TemplateLibrary? library = null)
    {
        var rig = new MealWeekRig();
        var docs = new DocsRig();
        rig.Box.Of(DocsData.Cards().Concat(MealPlanning.MealData.Box()).ToList());
        return new Rigs(rig, docs, new FakeTemplateStore(library ?? TemplateData.Full()), new FakeTemplateAuthor());
    }

    private static IEnumerable<MealItemRule> ItemRules(ExpectedDocument document) =>
        document.Pages.SelectMany(page => page.Rules).OfType<MealItemRule>();

    [Fact]
    public async Task ThePagesAreOnTheSheetAndInWhatTheReadBackExpects()
    {
        var rigs = Make();

        var report = await rigs.PlannedAsync();

        var templates = report.Templates!;
        Assert.Equal(TemplateRunStatus.Printed, templates.Status);
        var expected = Assert.Single(rigs.Docs.Expected.Recorded);
        Assert.Equal([1, 2, 3, 4], expected.Pages.Select(page => page.Number));
        var yesIds = ItemRules(expected).Select(rule => rule.YesBoxId).ToHashSet();
        Assert.All(rigs.Store.Library.Prints.Where(print => print.Week == Week), print => Assert.Contains(print.ItemId, yesIds));
        Assert.Equal(11, rigs.Store.Library.Prints.Count);
    }

    [Fact]
    public async Task EveryPrintedLineNamesItsTemplatesWords()
    {
        var rigs = Make();

        await rigs.PlannedAsync();

        var rules = ItemRules(rigs.Docs.Expected.Recorded.Single()).ToDictionary(rule => rule.YesBoxId);
        var lineIds = Enum.GetValues<MealTemplateSlot>()
            .SelectMany(slot => Enumerable.Range(0, slot is MealTemplateSlot.WeekdayBreakfast or MealTemplateSlot.WeekdayLunch ? 2 : 1).Select(line => TemplateItems.YesId(slot, line)))
            .ToList();
        Assert.Equal(11, lineIds.Count);
        Assert.Equal(lineIds.Order(StringComparer.Ordinal), rigs.Store.Library.Prints.Select(print => print.ItemId).Order(StringComparer.Ordinal));
        foreach (var print in rigs.Store.Library.Prints)
        {
            Assert.Contains(rigs.Store.Library.Find(print.TemplateId)!.Words, rules[print.ItemId].Subject, StringComparison.Ordinal);
            Assert.Equal(print.ItemId[..^"-yes".Length] + "-why", rules[print.ItemId].ReasonId);
        }
    }

    [Fact]
    public async Task WithoutATemplatesSeamTheSheetIsAsItWas()
    {
        var rigs = Make();

        var outcome = await Run.RunAsync(rigs.Inputs, rigs.Rig.Seams with { Documents = rigs.Docs.Seams }, CancellationToken.None);

        var report = Assert.IsType<MealWeekOutcome.Planned>(outcome).Report;
        Assert.Null(report.Templates);
        Assert.Equal(0, rigs.Store.Loads);
        Assert.Equal([1, 2], Assert.Single(rigs.Docs.Expected.Recorded).Pages.Select(page => page.Number));
    }

    [Fact]
    public async Task WithoutDocumentsTheLibraryIsNotTouched()
    {
        var rigs = Make();

        var outcome = await Run.RunAsync(rigs.Inputs, rigs.Rig.Seams with { Templates = new MealTemplateSeams(rigs.Store, rigs.Author) }, CancellationToken.None);

        Assert.Null(Assert.IsType<MealWeekOutcome.Planned>(outcome).Report.Templates);
        Assert.Equal(0, rigs.Store.Loads);
        Assert.Equal(0, rigs.Store.Saves);
    }

    [Fact]
    public async Task ThePartnerNeverTouchesTheLibrary()
    {
        var rigs = Make(TemplateRunData.Without(MealTemplateSlot.BoxSnack));
        rigs.Rig.MayPlan = false;

        var outcome = await Run.RunAsync(rigs.Inputs, rigs.Seams, CancellationToken.None);

        rigs.Rig.StoppedWith(MealNotPlannedBecause.NotThePlanner, outcome);
        Assert.Equal(0, rigs.Store.Loads);
        Assert.Equal(0, rigs.Store.Saves);
        Assert.Empty(rigs.Author.Requests);
    }

    [Fact]
    public async Task AWeekThatWasNotPlannedNeverTouchesTheLibrary()
    {
        var rigs = Make();
        rigs.Rig.Box.Of([]);

        var outcome = await Run.RunAsync(rigs.Inputs, rigs.Seams, CancellationToken.None);

        Assert.IsType<MealWeekOutcome.NotPlanned>(outcome);
        Assert.Equal(0, rigs.Store.Loads);
    }

    [Fact]
    public async Task TheLibraryIsKeptEvenWhenThePaperStops()
    {
        var rigs = Make();
        rigs.Docs.Roster.Answer = new MealRosterRead.Unavailable("The roster does not open.");

        var report = await rigs.PlannedAsync();

        Assert.IsType<MealDocumentsResult.Stopped>(report.Documents);
        Assert.Equal(1, rigs.Store.Saves);
        Assert.Equal(11, rigs.Store.Library.Prints.Count);
    }

    [Fact]
    public async Task AnUnchangedRunWritesNoNewLibraryAndNoNewPaper()
    {
        var rigs = Make();
        await rigs.PlannedAsync();
        var uploads = rigs.Docs.Tablet.Uploads;

        var again = await rigs.PlannedAsync();

        Assert.Equal(1, rigs.Store.Saves);
        Assert.Equal(uploads, rigs.Docs.Tablet.Uploads);
        Assert.Equal(TemplateRunStatus.Printed, again.Templates!.Status);
    }

    [Fact]
    public async Task NoApprovedTemplatesAndNoAuthorLeavesTwoPages()
    {
        var rigs = Make(TemplateLibrary.Empty);
        rigs.Author.Answer = new TemplateAuthored.Unavailable(Constitution.Domain.Workouts.AuthorUnavailable.NotConfigured);

        var report = await rigs.PlannedAsync();

        Assert.Equal(TemplateRunStatus.NothingYet, report.Templates!.Status);
        Assert.Equal(0, rigs.Store.Saves);
        Assert.Equal([1, 2], Assert.Single(rigs.Docs.Expected.Recorded).Pages.Select(page => page.Number));
    }

    [Fact]
    public async Task TheRunsTrailSaysWhatHappenedWithoutNamingAnything()
    {
        var rigs = Make();

        await rigs.PlannedAsync();

        Assert.Contains("meal templates: 0 templates proposed, 11 templates printed", rigs.Rig.Log.Phrases);
        Assert.DoesNotContain(rigs.Rig.Log.Phrases, line => line.Contains("oats", StringComparison.Ordinal));
    }
}
