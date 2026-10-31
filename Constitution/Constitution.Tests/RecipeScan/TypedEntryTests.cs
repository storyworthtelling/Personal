using Constitution.Domain.Notices;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// Typing a card in (C-6.5.4): three plain fields become the same words a scan gives, so the same check-and-keep step takes them.
/// Letting the words go is undo-first, like every other discard on the page.
/// </summary>
public class TypedEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 18, 0, 0, TimeSpan.Zero);

    private static TypedEntry Filled()
    {
        var entry = new TypedEntry
        {
            Title = "  Pear porridge ",
            IngredientsText = "2 pears, diced\r\n\r\n1 cup oats  \n2 cups milk",
            StepsText = "Simmer the oats and milk for 10 min.\nFold in the pears.",
        };
        return entry;
    }

    [Fact]
    public void LinesBecomeTheWordsAScanGives()
    {
        var typed = Filled().Read();

        Assert.Null(typed.Sentence);
        Assert.Equal("Pear porridge", typed.Payload!.Title);
        Assert.Equal(["2 pears, diced", "1 cup oats", "2 cups milk"], typed.Payload.Ingredients);
        Assert.Equal(["Simmer the oats and milk for 10 min.", "Fold in the pears."], typed.Payload.Steps);
    }

    [Fact]
    public void NothingTypedIsRefusedInWords()
    {
        var typed = new TypedEntry().Read();

        Assert.Null(typed.Payload);
        Assert.Equal(TypedEntry.NothingTypedSentence, typed.Sentence);
    }

    [Fact]
    public void ATitleAloneIsNotACard()
    {
        var typed = new TypedEntry { Title = "Pear porridge" }.Read();

        Assert.Null(typed.Payload);
        Assert.Equal(TypedEntry.NothingTypedSentence, typed.Sentence);
    }

    [Fact]
    public void TooLongAListNamesTheFieldNotTheWords()
    {
        var lines = string.Join('\n', Enumerable.Range(0, RecipeReadContract.MaxLines + 1).Select(n => "line " + n));
        var typed = new TypedEntry { Title = "Pear porridge", IngredientsText = lines }.Read();

        Assert.Null(typed.Payload);
        Assert.Contains("ingredients", typed.Sentence!, StringComparison.Ordinal);
        Assert.DoesNotContain("line 3", typed.Sentence!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWordsGoThroughTheSameConfirmStep()
    {
        var payload = Filled().Read().Payload!;
        var draft = ScanConfirmModel.From(payload, null, ScanOrigin.Typed);
        var keeper = new RecordingKeeper();

        var kept = draft.Keep(keeper, new HashSet<RecipeId>());

        Assert.Equal(ScanKeepOutcome.Kept, kept.Outcome);
        Assert.Equal(RecipeSource.Typed, keeper.Kept.Single().SourceLabel);
        Assert.Equal("Pear porridge", keeper.Kept.Single().Title);
    }

    [Fact]
    public void ATypedDraftSpeaksAsTypedNotScanned()
    {
        var draft = ScanConfirmModel.From(Filled().Read().Payload!, null, ScanOrigin.Typed);

        Assert.Equal("Just typed", draft.Label);
        Assert.Equal("Type again", draft.ScanAgainAction);
        Assert.DoesNotContain("picture", draft.StatusSentence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Looks right", draft.KeepAction);
    }

    [Fact]
    public void AScannedDraftIsAsItWas()
    {
        var draft = ScanConfirmModel.From(ScanSamples.AppleCrisp());

        Assert.Equal("Just scanned", draft.Label);
        Assert.Equal("Scan again", draft.ScanAgainAction);
        Assert.Equal("The picture isn’t kept, only the words.", draft.StatusSentence);
    }

    [Fact]
    public void LettingGoWaitsOutTheWindowAndTheFoldBringsItBack()
    {
        var board = new NoticeBoard();
        var undo = new TypedEntryUndo();
        var entry = Filled();

        Assert.True(undo.Begin(board, entry, Now));

        Assert.True(entry.IsEmpty);
        Assert.NotNull(board.Fold(Now.AddSeconds(1)));
        var act = board.TurnUndo(Now.AddSeconds(2));
        var back = undo.Reverse(act!);

        Assert.NotNull(back);
        Assert.Equal("Pear porridge", back!.Title.Trim());
        Assert.Contains("1 cup oats", back.IngredientsText, StringComparison.Ordinal);
        Assert.Null(undo.Pending);
    }

    [Fact]
    public void LettingGoOfNothingStartsNoWindow()
    {
        var board = new NoticeBoard();
        var undo = new TypedEntryUndo();

        Assert.False(undo.Begin(board, new TypedEntry(), Now));
        Assert.Null(undo.Pending);
        Assert.Null(board.Fold(Now.AddSeconds(1)));
    }

    [Fact]
    public void AnotherActsKeyReturnsNothing()
    {
        var undo = new TypedEntryUndo();
        undo.Begin(new NoticeBoard(), Filled(), Now);

        Assert.Null(undo.Reverse("recipe-scan:discard"));
        Assert.NotNull(undo.Pending);
    }

    [Fact]
    public void WhenTheWindowClosesTheWordsAreForgotten()
    {
        var undo = new TypedEntryUndo();
        undo.Begin(new NoticeBoard(), Filled(), Now);

        Assert.False(undo.Expire(Now.AddSeconds(1)));
        Assert.True(undo.Expire(Now + NoticeBoard.UndoWindow + TimeSpan.FromSeconds(1)));
        Assert.Null(undo.Pending);
        Assert.False(undo.Expire(Now + NoticeBoard.UndoWindow + TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void ASecondLetGoMakesTheFirstPermanent()
    {
        var undo = new TypedEntryUndo();
        undo.Begin(new NoticeBoard(), Filled(), Now);
        var second = new TypedEntry { Title = "Plum jam", IngredientsText = "1 kg plums" };
        undo.Begin(new NoticeBoard(), second, Now.AddSeconds(3));

        Assert.Equal("Plum jam", undo.Pending!.Title);
    }
}
