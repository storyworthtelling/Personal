using Constitution.Domain.Delivery;
using Constitution.Domain.Ink;
using Constitution.Domain.Notices;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Waiting;
using Constitution.Infrastructure.ReadBack;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// A document's saved result, kept before its page is trashed. Whatever was resolved has to come back out of the file
/// as the same resolution, because later features read these as the owner's answers; and a question not yet answered
/// has to survive the app being restarted, since the notice board lives only as long as the process.
/// </summary>
public sealed class FileResolutionStoreTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("constitution-results-");

    private FileResolutionStore Make() => new(Path.Combine(folder.FullName, "results"));

    public void Dispose() => folder.Delete(recursive: true);

    public static TheoryData<InkResolution> EveryKind => new()
    {
        new InkResolution.Ticked("done"),
        new InkResolution.Skipped("done"),
        new InkResolution.Denied("dinner-1", "no time to cook"),
        new InkResolution.Denied("dinner-1", null),
        new InkResolution.ApprovedAtDeadline("dinner-1"),
        new InkResolution.Unchanged("feel"),
        new InkResolution.Better("sore"),
        new InkResolution.StillSore("sore"),
        new InkResolution.NoNeed("oil"),
        new InkResolution.Needed("oil", "two bottles"),
        new InkResolution.Needed("oil", null),
        new InkResolution.Circled("feel", "right"),
        new InkResolution.Written("result", "12"),
        new InkResolution.PageDone("done", "easy"),
        new InkResolution.NeedsAsking("done", "Goblet squat", "Ticked", "Not ticked", "read:Goblet squat:Ticked|Not ticked"),
        new InkResolution.Undecided("dinner-1"),
        new InkResolution.NotRead("done", "The read did not report 'done'."),
    };

    private static DocumentResult ResultOf(string name, params InkResolution[] page2) => new(
        name, DeliveryKind.Workout, Day, Person, Morning,
        [new PageResolutions(2, page2), new PageResolutions(3, [new InkResolution.Skipped("done")])]);

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void EveryKindOfResolutionComesBackAsItself(InkResolution resolution)
    {
        var store = Make();
        var result = ResultOf("Constitution — Workout — 2026-11-03", resolution);

        store.Save(result);

        var loaded = store.Load(result.Name)!;
        Assert.Equal(result.Name, loaded.Name);
        Assert.Equal(result.Kind, loaded.Kind);
        Assert.Equal(result.Day, loaded.Day);
        Assert.Equal(result.Person, loaded.Person);
        Assert.Equal(result.ReadAt, loaded.ReadAt);
        Assert.Equal(result.Pages.Select(page => page.Number), loaded.Pages.Select(page => page.Number));
        Assert.Equal(resolution, loaded.Pages[0].Resolutions.Single());
        Assert.Equal(new InkResolution.Skipped("done"), loaded.Pages[1].Resolutions.Single());
    }

    [Fact]
    public void EveryKindTheResolverCanMakeIsCoveredByTheTheory()
    {
        var made = typeof(InkResolution).GetNestedTypes().Where(type => typeof(InkResolution).IsAssignableFrom(type)).Select(type => type.Name).ToHashSet();
        var covered = ((IEnumerable<object[]>)EveryKind).Select(row => ((InkResolution)row[0]).GetType().Name).ToHashSet();

        Assert.NotEmpty(made);
        Assert.Equal(made.Order(), covered.Order());
    }

    [Fact]
    public void NothingSavedLoadsAsNull() => Assert.Null(Make().Load("Constitution — Workout — 2026-11-03"));

    [Fact]
    public void SavingAgainReplacesTheEarlierResult()
    {
        var store = Make();
        var name = "Constitution — Workout — 2026-11-03";
        store.Save(ResultOf(name, new InkResolution.Skipped("done")));

        store.Save(ResultOf(name, new InkResolution.Ticked("done")));

        Assert.Equal(new InkResolution.Ticked("done"), store.Load(name)!.Pages[0].Resolutions.Single());
        Assert.Single(Directory.GetFiles(Path.Combine(folder.FullName, "results"), "*.json"));
    }

    [Fact]
    public void TwoDocumentsWhoseNamesLookAlikeDoNotShareAFile()
    {
        var store = Make();
        store.Save(ResultOf("Constitution — Workout — 2026-11-03 — A b", new InkResolution.Ticked("done")));
        store.Save(ResultOf("Constitution — Workout — 2026-11-03 — A-b", new InkResolution.Skipped("done")));

        Assert.Equal(new InkResolution.Ticked("done"), store.Load("Constitution — Workout — 2026-11-03 — A b")!.Pages[0].Resolutions.Single());
        Assert.Equal(new InkResolution.Skipped("done"), store.Load("Constitution — Workout — 2026-11-03 — A-b")!.Pages[0].Resolutions.Single());
    }

    [Fact]
    public void AnUnreadableFileIsSetAside()
    {
        var store = Make();
        var name = "Constitution — Workout — 2026-11-03";
        store.Save(ResultOf(name, new InkResolution.Ticked("done")));
        var file = Directory.GetFiles(Path.Combine(folder.FullName, "results"), "*.json").Single();
        File.WriteAllText(file, "{ torn");

        Assert.Null(store.Load(name));

        Assert.False(File.Exists(file));
        Assert.Equal("{ torn", File.ReadAllText(file + ".unreadable"));
    }

    [Fact]
    public void ANewerResolutionKindSetsTheFileAside()
    {
        var store = Make();
        var name = "Constitution — Workout — 2026-11-03";
        store.Save(ResultOf(name, new InkResolution.Ticked("done")));
        var file = Directory.GetFiles(Path.Combine(folder.FullName, "results"), "*.json").Single();
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"ticked\"", "\"levitated\"", StringComparison.Ordinal));

        Assert.Null(store.Load(name));
        Assert.True(File.Exists(file + ".unreadable"));
    }

    // ---- questions waiting ----

    private static InkResolution.NeedsAsking Question(string subject) =>
        new("done", subject, "Ticked", "Not ticked", LowConfidenceCause.Key(subject, "Ticked", "Not ticked"));

    [Fact]
    public void AQuestionIsPendingUntilItIsAnswered()
    {
        var store = Make();
        var name = "Constitution — Workout — 2026-11-03";
        store.Save(ResultOf(name, Question("Goblet squat"), new InkResolution.Ticked("elsewhere")));

        var pending = Assert.Single(store.PendingQuestions());

        Assert.Equal(name, pending.DocumentName);
        Assert.Equal(2, pending.PageNumber);
        Assert.Equal(Question("Goblet squat"), pending.Question);
        Assert.Equal(Morning, pending.ReadAt);

        store.MarkAnswered(pending.Question.CauseKey, "Ticked");

        Assert.Empty(store.PendingQuestions());
    }

    [Fact]
    public void AnAnswerIsRememberedAcrossRuns()
    {
        var name = "Constitution — Workout — 2026-11-03";
        Make().Save(ResultOf(name, Question("Goblet squat")));
        Make().MarkAnswered(Question("Goblet squat").CauseKey, "Not ticked");

        Assert.Empty(Make().PendingQuestions());
    }

    [Fact]
    public void AnAnswerDoesNotSilenceNextWeeksQuestion()
    {
        var store = Make();
        store.Save(ResultOf("Constitution — Workout — 2026-11-03", Question("Goblet squat")));
        store.MarkAnswered(Question("Goblet squat").CauseKey, "Ticked");

        store.Save(ResultOf("Constitution — Workout — 2026-11-10", Question("Goblet squat")));

        var pending = Assert.Single(store.PendingQuestions());
        Assert.Equal("Constitution — Workout — 2026-11-10", pending.DocumentName);
    }

    [Fact]
    public void ReSavingTheSameDocumentKeepsAnAnswerAlreadyGiven()
    {
        var store = Make();
        var name = "Constitution — Workout — 2026-11-03";
        store.Save(ResultOf(name, Question("Goblet squat")));
        store.MarkAnswered(Question("Goblet squat").CauseKey, "Ticked");

        store.Save(ResultOf(name, Question("Goblet squat")));

        Assert.Empty(store.PendingQuestions());
    }

    [Fact]
    public void ThePickIsKeptWithTheResultThatAskedIt()
    {
        var store = Make();
        var name = "Constitution — Workout — 2026-11-03";
        store.Save(ResultOf(name, Question("Goblet squat")));
        store.Save(ResultOf("Constitution — Workout — 2026-11-10", Question("Band row")));

        store.MarkAnswered(Question("Goblet squat").CauseKey, "Not ticked");

        var kept = Make().AnswersFor(name);
        Assert.Equal("Not ticked", kept[Question("Goblet squat").CauseKey]);
        Assert.Single(kept);
        Assert.Empty(Make().AnswersFor("Constitution — Workout — 2026-11-10"));
        Assert.Empty(Make().AnswersFor("Constitution — Workout — 2020-01-01"));
    }

    [Fact]
    public void AnAnswerNeedsAPick()
    {
        Assert.Throws<ArgumentException>(() => Make().MarkAnswered(Question("Goblet squat").CauseKey, " "));
        Assert.Throws<ArgumentException>(() => Make().MarkAnswered(" ", "Ticked"));
    }

    [Fact]
    public void ADocumentWithNoQuestionsHasNothingPending()
    {
        var store = Make();
        store.Save(ResultOf("Constitution — Workout — 2026-11-03", new InkResolution.Ticked("done")));

        Assert.Empty(store.PendingQuestions());
    }

    [Fact]
    public void RestoredQuestionsKeepTheirOwnDate()
    {
        var store = Make();
        store.Save(ResultOf("Constitution — Workout — 2026-11-03", Question("Goblet squat")));
        var board = new NoticeBoard();

        var raised = ReadBackQuestions.Raise(board, store);

        Assert.Equal(1, raised);
        var notice = Assert.Single(board.Waiting());
        Assert.Equal(NoticeKind.LowConfidenceRead, notice.Kind);
        Assert.Equal(Question("Goblet squat").CauseKey, notice.CauseKey);
        Assert.Equal(Morning, notice.CreatedAt);
        Assert.Equal(0, ReadBackQuestions.Raise(board, store));
    }

    [Fact]
    public void AnAnsweredQuestionIsNotRestored()
    {
        var store = Make();
        store.Save(ResultOf("Constitution — Workout — 2026-11-03", Question("Goblet squat")));
        store.MarkAnswered(Question("Goblet squat").CauseKey, "Ticked");
        var board = new NoticeBoard();

        Assert.Equal(0, ReadBackQuestions.Raise(board, store));
        Assert.Empty(board.Waiting());
    }
}
