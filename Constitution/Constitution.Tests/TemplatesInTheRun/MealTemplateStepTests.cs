using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Constitution.Tests.Templates;
using static Constitution.Tests.TemplatesInTheRun.TemplateRunData;

namespace Constitution.Tests.TemplatesInTheRun;

/// <summary>The templates step on its own, over a fake library and a fake author: Sam and Lee's household, invented dishes.</summary>
public class MealTemplateStepTests
{
    [Fact]
    public async Task NoHouseholdFileIsNoLibraryYet()
    {
        var store = new FakeTemplateStore { Answer = new TemplateLibraryRead.NoneYet("Waiting for the household file.") };
        var author = new FakeTemplateAuthor();

        var result = await RunAsync(store, author);

        Assert.Equal(TemplateRunStatus.NoLibraryYet, result.Status);
        Assert.Null(result.Daily);
        Assert.Empty(author.Requests);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task AnUnreadableLibraryIsNeverAnEmptyOne()
    {
        var store = new FakeTemplateStore { Answer = new TemplateLibraryRead.Unreadable("The household file does not open.") };
        var author = new FakeTemplateAuthor();

        var result = await RunAsync(store, author);

        Assert.Equal(TemplateRunStatus.LibraryUnreadable, result.Status);
        Assert.Null(result.Daily);
        Assert.Empty(author.Requests);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task NoAuthorAndNothingApprovedPrintsNothing()
    {
        var store = new FakeTemplateStore();

        var result = await RunAsync(store, new FakeTemplateAuthor { Answer = new TemplateAuthored.Unavailable(Constitution.Domain.Workouts.AuthorUnavailable.NotConfigured) });

        Assert.Equal(TemplateRunStatus.NothingYet, result.Status);
        Assert.Null(result.Daily);
        Assert.Equal(0, result.Printed);
        Assert.Equal(Constitution.Domain.Workouts.AuthorUnavailable.NotConfigured, result.AuthorReason);
        Assert.Equal(0, store.Saves);
        Assert.Empty(store.Library.Prints);
    }

    [Fact]
    public async Task TheRealNoAuthorLeavesTheLibraryAlone()
    {
        var store = new FakeTemplateStore();

        var result = await MealTemplateStepRunner.With(store, new NoTemplateAuthor());

        Assert.Equal(TemplateRunStatus.NothingYet, result.Status);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task AFullLibraryPrintsEveryLineAndRemembersIt()
    {
        var store = new FakeTemplateStore(Full());

        var result = await RunAsync(store);

        Assert.Equal(TemplateRunStatus.Printed, result.Status);
        Assert.Equal(11, result.Printed);
        Assert.Equal(["Sam", "Lee"], result.Daily!.WeekdayBreakfasts.Select(line => line.Label));
        Assert.Equal(["Sam", "Lee"], result.Daily.WeekdayLunches.Select(line => line.Label));
        Assert.Equal(1, store.Saves);
        Assert.Equal(11, store.Library.Prints.Count(print => print.Week == Week));
    }

    [Fact]
    public async Task AnUnchangedSecondRunWritesNothing()
    {
        var store = new FakeTemplateStore(Full());
        var first = await RunAsync(store);

        var second = await RunAsync(store);

        Assert.Equal(1, store.Saves);
        Assert.Equal(first.Daily!.WeekendBreakfast, second.Daily!.WeekendBreakfast);
        Assert.Equal(first.Daily.WeekdayLunches, second.Daily.WeekdayLunches);
    }

    [Fact]
    public async Task ThePrintsComeBackThroughTheRotationAsTheyWere()
    {
        var store = new FakeTemplateStore(Full());
        var picked = TemplateRotation.Pick(Full(), Week, new TemplateRotationFacts(["Sam", "Lee"], ["Sam", "Lee"]));

        await RunAsync(store);

        Assert.Equal(
            picked.Prints.OrderBy(print => print.ItemId, StringComparer.Ordinal),
            store.Library.Prints.Where(print => print.Week == Week).OrderBy(print => print.ItemId, StringComparer.Ordinal));
    }

    [Fact]
    public async Task LastWeeksPrintsAreHandedToTheRotation()
    {
        var facts = new TemplateRotationFacts(["Sam", "Lee"], ["Sam", "Lee"]);
        var thisWeek = TemplateRotation.Pick(Full(), Week, facts);
        var claimed = new TemplatePrint(Week.AddDays(-7), "weekend-lunch-yes", thisWeek.Prints.Single(print => print.ItemId == "weekend-lunch-yes").TemplateId);
        var store = new FakeTemplateStore(Full().WithPrints([claimed]));

        await RunAsync(store);

        var now = store.Library.Prints.Single(print => print.Week == Week && print.ItemId == "weekend-lunch-yes");
        Assert.NotEqual(claimed.TemplateId, now.TemplateId);
    }

    [Fact]
    public async Task TheAuthorIsAskedForOnlyWhatIsShort()
    {
        var store = new FakeTemplateStore(Without(MealTemplateSlot.WeekendLunch));
        var author = new FakeTemplateAuthor();

        await RunAsync(store, author);

        var request = Assert.Single(author.Requests);
        Assert.Equal([MealTemplateSlot.WeekendLunch], request.Wanted);
        Assert.Equal(3, request.Count);
        Assert.Equal(24, request.Approved.Count);
    }

    [Fact]
    public async Task ProposalsAreMergedAsWaitingAndOneIsPrinted()
    {
        var store = new FakeTemplateStore(Without(MealTemplateSlot.WeekendLunch));
        var author = new FakeTemplateAuthor
        {
            Answer = new TemplateAuthored.Drafts(
            [
                new TemplateDraft(MealTemplateSlot.WeekendLunch, "Veggie quesadillas"),
                new TemplateDraft(MealTemplateSlot.WeekendLunch, "Tomato soup and toast"),
            ]),
        };

        var result = await RunAsync(store, author);

        Assert.Equal(TemplateRunStatus.Printed, result.Status);
        Assert.Equal(2, result.Proposed);
        Assert.Equal("Tomato soup and toast", result.Daily!.WeekendLunch);
        Assert.Equal(2, store.Library.Proposed.Count);
        Assert.Empty(store.Library.Approved.Where(template => template.Slot == MealTemplateSlot.WeekendLunch));
        Assert.Single(store.Library.Prints.Where(print => print.ItemId == "weekend-lunch-yes"));
    }

    [Fact]
    public async Task ARevisedSheetReprintsTheSameProposal()
    {
        var store = new FakeTemplateStore(Without(MealTemplateSlot.WeekendLunch));
        var author = new FakeTemplateAuthor { Answer = new TemplateAuthored.Drafts([new TemplateDraft(MealTemplateSlot.WeekendLunch, "Veggie quesadillas")]) };
        var first = await RunAsync(store, author);
        author.Answer = new TemplateAuthored.Drafts([new TemplateDraft(MealTemplateSlot.WeekendLunch, "Pasta with peas")]);

        var again = await RunAsync(store, author);

        Assert.Equal("Veggie quesadillas", again.Daily!.WeekendLunch);
        Assert.Equal(first.Daily!.WeekendLunch, again.Daily.WeekendLunch);
        Assert.Single(author.Requests);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task AStockedLibraryNeverAsksTheAuthor()
    {
        var author = new FakeTemplateAuthor();

        await RunAsync(new FakeTemplateStore(Full()), author);

        Assert.Empty(author.Requests);
    }

    [Fact]
    public async Task TheCapOfWaitingProposalsStopsTheAsk()
    {
        var waiting = Enum.GetValues<MealTemplateSlot>().Take(TemplateProposals.MaxOutstanding)
            .Select((slot, index) => TemplateData.Proposed($"tpl-wait-{index}", slot, $"Waiting dish {index}")).ToArray();
        var store = new FakeTemplateStore(new TemplateLibrary([.. waiting], []));
        var author = new FakeTemplateAuthor();

        await RunAsync(store, author);

        Assert.Empty(author.Requests);
    }

    [Fact]
    public async Task AWeekAlreadyPrintedIsNotAskedAgain()
    {
        var store = new FakeTemplateStore(Without(MealTemplateSlot.BoxSnack).WithPrints([new TemplatePrint(Week, "box-snack-yes", "tpl-box-snack-x")]));
        var author = new FakeTemplateAuthor();

        await RunAsync(store, author);

        Assert.Empty(author.Requests);
    }

    [Fact]
    public async Task AnAuthorThatCannotAnswerIsReportedAndHarmless()
    {
        var store = new FakeTemplateStore(Without(MealTemplateSlot.BoxSnack));
        var author = new FakeTemplateAuthor { Answer = new TemplateAuthored.Unavailable(Constitution.Domain.Workouts.AuthorUnavailable.Offline) };

        var result = await RunAsync(store, author);

        Assert.Equal(TemplateRunStatus.Incomplete, result.Status);
        Assert.Equal(Constitution.Domain.Workouts.AuthorUnavailable.Offline, result.AuthorReason);
        Assert.Null(result.Daily);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task WordsTheLibraryAlreadyHoldsAreNotProposedAgain()
    {
        var library = Without(MealTemplateSlot.BoxSnack).With(TemplateData.Rejected("tpl-box-snack-pretzels", MealTemplateSlot.BoxSnack, "Pretzels"));
        var store = new FakeTemplateStore(library);
        var author = new FakeTemplateAuthor { Answer = new TemplateAuthored.Drafts([new TemplateDraft(MealTemplateSlot.BoxSnack, " pretzels ")]) };

        var result = await RunAsync(store, author);

        Assert.Equal(0, result.Proposed);
        Assert.Equal(TemplateRunStatus.Incomplete, result.Status);
        Assert.Empty(store.Library.Proposed);
    }

    [Fact]
    public async Task AnIncompleteLibraryStillKeepsWhatWasProposed()
    {
        var store = new FakeTemplateStore(new TemplateLibrary([], []));
        var author = new FakeTemplateAuthor { Answer = new TemplateAuthored.Drafts([new TemplateDraft(MealTemplateSlot.BoxFruit, "Cold melon")]) };

        var result = await RunAsync(store, author);

        Assert.Equal(TemplateRunStatus.Incomplete, result.Status);
        Assert.Null(result.Daily);
        Assert.Equal(1, result.Proposed);
        Assert.Equal(1, store.Saves);
        Assert.Single(store.Library.Proposed);
        Assert.Empty(store.Library.Prints);
    }

    [Fact]
    public async Task AfterTheDeadlineNothingNewIsPrinted()
    {
        var store = new FakeTemplateStore(Full());
        var author = new FakeTemplateAuthor();

        var result = await RunAsync(store, author, After);

        Assert.Equal(TemplateRunStatus.PastDeadline, result.Status);
        Assert.Null(result.Daily);
        Assert.Equal(0, store.Saves);
        Assert.Empty(author.Requests);
    }

    [Fact]
    public async Task AfterTheDeadlineAPrintedWeekIsStillTheSame()
    {
        var store = new FakeTemplateStore(Full());
        var first = await RunAsync(store);

        var later = await RunAsync(store, now: After);

        Assert.Equal(TemplateRunStatus.Printed, later.Status);
        Assert.Equal(first.Daily!.WeekendBreakfast, later.Daily!.WeekendBreakfast);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task ARefusedSaveMeansNothingIsPrinted()
    {
        var store = new FakeTemplateStore(Full()) { RefuseWith = "The household file does not open." };

        var result = await RunAsync(store);

        Assert.Equal(TemplateRunStatus.NotSaved, result.Status);
        Assert.Null(result.Daily);
        Assert.Equal(0, result.Printed);
        Assert.Equal(0, result.Proposed);
        Assert.Empty(store.Library.Prints);
    }

    [Fact]
    public async Task UnreadableRecordsHoldTheAuthorBack()
    {
        var store = new FakeTemplateStore { Answer = new TemplateLibraryRead.Held(Without(MealTemplateSlot.BoxSnack), 2) };
        var author = new FakeTemplateAuthor();

        var result = await RunAsync(store, author);

        Assert.Empty(author.Requests);
        Assert.Equal(2, result.UnreadableRecords);
        Assert.Equal(TemplateRunStatus.Incomplete, result.Status);
    }

    [Fact]
    public async Task OneTrailLineIsWrittenPerRun()
    {
        var log = new RecordingLog();

        await RunAsync(new FakeTemplateStore(Full()), log: log);

        Assert.Equal("meal templates: 0 templates proposed, 11 templates printed", Assert.Single(log.Lines));
    }
}

internal static class MealTemplateStepRunner
{
    public static Task<MealTemplatesResult> With(FakeTemplateStore store, ITemplateAuthor author) =>
        Constitution.Infrastructure.Meals.MealTemplateStep.RunAsync(
            Week,
            Constitution.Tests.Calendar.InventedFamily.Rules(),
            Before,
            new Constitution.Infrastructure.Meals.MealTemplateSeams(store, author),
            new RecordingLog(),
            CancellationToken.None);
}
