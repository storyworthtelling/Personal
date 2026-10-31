using Constitution.Domain.Meals;
using Constitution.Domain.Notices;
using static Constitution.Tests.AteSomethingElse.AteFixture;

namespace Constitution.Tests.AteSomethingElse;

/// <summary>C-6.6.1 (book p.15): one photo, straight back to the card, an estimate inline, no toast and no screen between.</summary>
public class AteSomethingElseFlowTests
{
    private static async Task<(AteSomethingElseModel Model, AteEstimate? Recorded)> RunToTheEnd(AteOutcome outcome)
    {
        using var rig = new PhotoRig();
        var model = new AteSomethingElseModel();
        model.StartCapture(Evening);
        var read = await model.EstimateAsync(new FakeEstimator(_ => Task.FromResult(outcome)), rig.CacheRoot, rig.PhotoPath, rig.Delete);
        return (model, model.Finish(read));
    }

    [Fact]
    public void NoScreenBetween()
    {
        // The whole route is four phases and none of them is a screen: photo, estimating on the card, the result on the card.
        Assert.Equal(["Idle", "Capturing", "Estimating", "Shown", "Said"], Enum.GetNames<AtePhase>());
        Assert.DoesNotContain(Enum.GetNames<AtePhase>(), name => name.Contains("Confirm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheCardSaysEstimatingThenTheFigure()
    {
        using var rig = new PhotoRig();
        var model = new AteSomethingElseModel();
        model.StartCapture(Evening);
        Assert.Equal(AtePhase.Capturing, model.Phase);
        Assert.Null(model.LineFor(AteMeal.Dinner, model.Day));

        var gate = new TaskCompletionSource<AteOutcome>();
        var pending = model.EstimateAsync(new FakeEstimator(_ => gate.Task), rig.CacheRoot, rig.PhotoPath, rig.Delete);

        Assert.Equal(AtePhase.Estimating, model.Phase);
        var status = model.LineFor(AteMeal.Dinner, model.Day)!;
        Assert.Equal("Estimating your dinner…", status.Text);
        Assert.True(status.IsStatus);
        Assert.Null(status.Figure);

        gate.SetResult(new AteOutcome.Read(Reading()));
        model.Finish(await pending);

        Assert.Equal(AtePhase.Shown, model.Phase);
    }

    [Fact]
    public async Task ResultIsInlineNoToast()
    {
        var (model, recorded) = await RunToTheEnd(new AteOutcome.Read(Reading()));

        var line = model.LineFor(AteMeal.Dinner, new DateOnly(2026, 11, 17))!;
        Assert.Equal("You had something else · about 900 kcal", line.Text);
        Assert.Equal("900 kcal", line.Figure);
        Assert.Equal("You had something else · about ", line.Lead);
        Assert.False(line.IsStatus);
        Assert.NotNull(recorded);
    }

    // D107: the two tests that pinned the detail line (uncertainty in words, confidence in words) are replaced by these.
    [Fact]
    public async Task NoDetailLineOnTheCard()
    {
        var (model, _) = await RunToTheEnd(new AteOutcome.Read(Reading()));

        var line = model.LineFor(AteMeal.Dinner, new DateOnly(2026, 11, 17))!;

        Assert.DoesNotContain(typeof(AteLine).GetProperties(), property => property.Name == "Detail");
        Assert.Equal("You had something else · about 900 kcal", line.Text);
        Assert.DoesNotContain("guess", line.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("750", line.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("pasta", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRecordStillHoldsRangeAndConfidence()
    {
        var (model, recorded) = await RunToTheEnd(new AteOutcome.Read(Reading(confidence: AteConfidence.Good)));

        Assert.Equal(750, recorded!.Reading.KcalLow);
        Assert.Equal(1050, recorded.Reading.KcalHigh);
        Assert.Equal(AteConfidence.Good, recorded.Reading.Confidence);
        Assert.Equal("pasta with tomato sauce", recorded.Reading.What);
        Assert.Equal(25, recorded.Reading.ProteinLow);
        Assert.NotNull(model.LineFor(AteMeal.Dinner, recorded.Day));
    }

    [Fact]
    public void OnlyTheDinnerCardExists()
    {
        var model = new AteSomethingElseModel();
        var day = new DateOnly(2026, 11, 17);
        model.Load([Estimate(AteMeal.Breakfast), Estimate(AteMeal.Lunch), Estimate()]);

        Assert.Null(model.LineFor(AteMeal.Breakfast, day));
        Assert.Null(model.LineFor(AteMeal.Lunch, day));
        Assert.NotNull(model.LineFor(AteMeal.Dinner, day));
        Assert.Equal([AteMeal.Dinner], model.MealsWithALine(day));
    }

    [Theory]
    [InlineData(8, "Noted for breakfast.")]
    [InlineData(13, "Noted for lunch.")]
    public async Task BreakfastAndLunchGiveOnlyTheStatusSentence(int hour, string sentence)
    {
        using var rig = new PhotoRig();
        var model = new AteSomethingElseModel();
        var when = new DateTimeOffset(2026, 11, 17, hour, 5, 0, TimeSpan.FromHours(-5));
        model.StartCapture(when);
        var read = await model.EstimateAsync(new FakeEstimator(_ => Task.FromResult<AteOutcome>(new AteOutcome.Read(Reading()))), rig.CacheRoot, rig.PhotoPath, rig.Delete);

        var recorded = model.Finish(read);

        Assert.NotNull(recorded);
        Assert.Equal(sentence, model.StatusFor(model.Day));
        Assert.Empty(model.MealsWithALine(model.Day));
        Assert.Null(model.LineFor(AteMeal.Breakfast, model.Day));
        Assert.Null(model.LineFor(AteMeal.Lunch, model.Day));
        Assert.Equal(750, recorded!.Reading.KcalLow);
    }

    [Fact]
    public async Task ADinnerPhotoHasNoStatusSentenceBeyondItsCard()
    {
        var (model, _) = await RunToTheEnd(new AteOutcome.Read(Reading()));

        Assert.Null(model.StatusFor(model.Day));
    }

    [Fact]
    public async Task AMorningFailureIsStillSaidInTheStatusLine()
    {
        using var rig = new PhotoRig();
        var model = new AteSomethingElseModel();
        model.StartCapture(new DateTimeOffset(2026, 11, 17, 8, 0, 0, TimeSpan.FromHours(-5)));
        var read = await model.EstimateAsync(new FakeEstimator(_ => Task.FromResult<AteOutcome>(new AteOutcome.NoMeal())), rig.CacheRoot, rig.PhotoPath, rig.Delete);
        model.Finish(read);

        Assert.Equal(AteText.NoMealSentence, model.StatusFor(model.Day));
    }

    [Fact]
    public async Task OnlyTheMealTheTimeNamedShowsTheLine()
    {
        var (model, _) = await RunToTheEnd(new AteOutcome.Read(Reading()));

        Assert.Null(model.LineFor(AteMeal.Lunch, new DateOnly(2026, 11, 17)));
        Assert.Null(model.LineFor(AteMeal.Dinner, new DateOnly(2026, 11, 18)));
    }

    [Fact]
    public async Task NothingIsRecordedWhenThereWasNoMeal()
    {
        var (model, recorded) = await RunToTheEnd(new AteOutcome.NoMeal());

        Assert.Null(recorded);
        Assert.Equal(AtePhase.Said, model.Phase);
        Assert.Equal(AteText.NoMealSentence, model.LineFor(AteMeal.Dinner, model.Day)!.Text);
    }

    [Fact]
    public async Task AnUnreadableReplyIsCouldNotEstimateNeverZero()
    {
        var (model, recorded) = await RunToTheEnd(new AteOutcome.Unreadable());

        Assert.Null(recorded);
        var text = model.LineFor(AteMeal.Dinner, model.Day)!.Text;
        Assert.Equal(AteText.UnreadableSentence, text);
        Assert.DoesNotContain("kcal", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoKeyIsAStatusSentenceNotAGuess()
    {
        var (model, recorded) = await RunToTheEnd(new AteOutcome.Unavailable(AteText.NoKeySentence));

        Assert.Null(recorded);
        Assert.Equal(AteText.NoKeySentence, model.LineFor(AteMeal.Dinner, model.Day)!.Text);
        Assert.True(model.LineFor(AteMeal.Dinner, model.Day)!.IsStatus);
    }

    [Fact]
    public async Task TheUnavailableDefaultNeverGuesses()
    {
        using var rig = new PhotoRig();
        var model = new AteSomethingElseModel();
        model.StartCapture(Evening);

        var outcome = await model.EstimateAsync(new UnavailableAteEstimator(), rig.CacheRoot, rig.PhotoPath, rig.Delete);

        var said = Assert.IsType<AteOutcome.Unavailable>(outcome);
        Assert.Equal(AteText.NoKeySentence, said.Sentence);
        Assert.Null(model.Finish(outcome));
    }

    [Fact]
    public void BackingOutOfTheCameraIsQuiet()
    {
        var model = new AteSomethingElseModel();
        model.StartCapture(Evening);

        model.CaptureBackedOut();

        Assert.Equal(AtePhase.Idle, model.Phase);
        Assert.Null(model.LineFor(AteMeal.Dinner, model.Day));
        Assert.True(model.CanStart);
    }

    [Fact]
    public void AFailedCameraIsASentenceOnTheCard()
    {
        var model = new AteSomethingElseModel();
        model.StartCapture(Evening);

        model.CaptureFailed(AteText.CaptureFailedSentence);

        Assert.Equal(AteText.CaptureFailedSentence, model.LineFor(AteMeal.Dinner, model.Day)!.Text);
    }

    [Fact]
    public async Task OnePhotoAtATime()
    {
        using var rig = new PhotoRig();
        var model = new AteSomethingElseModel();
        Assert.True(model.CanStart);
        model.StartCapture(Evening);
        Assert.False(model.CanStart);

        var gate = new TaskCompletionSource<AteOutcome>();
        var pending = model.EstimateAsync(new FakeEstimator(_ => gate.Task), rig.CacheRoot, rig.PhotoPath, rig.Delete);
        Assert.False(model.CanStart);

        gate.SetResult(new AteOutcome.Read(Reading()));
        model.Finish(await pending);
        Assert.True(model.CanStart);
    }

    [Fact]
    public async Task TheStoredEstimatesComeBackOnTheCard()
    {
        var model = new AteSomethingElseModel();
        model.Load([Estimate()]);

        var line = model.LineFor(AteMeal.Dinner, new DateOnly(2026, 11, 17))!;

        Assert.Equal("You had something else · about 900 kcal", line.Text);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ALaterPhotoForTheSameMealReplacesTheEarlier()
    {
        var (model, _) = await RunToTheEnd(new AteOutcome.Read(Reading(low: 300, high: 450)));
        using var rig = new PhotoRig();
        model.StartCapture(Evening);

        var again = await model.EstimateAsync(new FakeEstimator(_ => Task.FromResult<AteOutcome>(new AteOutcome.Read(Reading(low: 1200, high: 1500)))), rig.CacheRoot, rig.PhotoPath, rig.Delete);
        model.Finish(again);

        Assert.Equal("You had something else · about 1,350 kcal", model.LineFor(AteMeal.Dinner, new DateOnly(2026, 11, 17))!.Text);
    }

    [Fact]
    public async Task ADiscardIsUndoFirstThroughTheDogEar()
    {
        var (model, recorded) = await RunToTheEnd(new AteOutcome.Read(Reading()));
        var board = new NoticeBoard();
        var undo = new AteUndo();
        var now = Evening;

        undo.Begin(board, recorded!, null, now);
        Assert.True(board.Fold(now)!.IsUndo);
        Assert.NotNull(undo.Pending);

        var step = undo.Reverse(AteUndo.ActKey)!;
        model.Withdraw(step.Recorded, step.Previous);

        Assert.Null(model.LineFor(AteMeal.Dinner, new DateOnly(2026, 11, 17)));
        Assert.Null(undo.Pending);
    }

    [Fact]
    public async Task UndoPutsBackTheEstimateItReplaced()
    {
        var (model, recorded) = await RunToTheEnd(new AteOutcome.Read(Reading(low: 1200, high: 1500)));
        var earlier = Estimate();
        var undo = new AteUndo();
        undo.Begin(new NoticeBoard(), recorded!, earlier, Evening);

        var step = undo.Reverse(AteUndo.ActKey)!;
        model.Withdraw(step.Recorded, step.Previous);

        Assert.Equal("You had something else · about 900 kcal", model.LineFor(AteMeal.Dinner, new DateOnly(2026, 11, 17))!.Text);
    }

    [Fact]
    public async Task TheWindowClosingKeepsTheEstimate()
    {
        var (_, recorded) = await RunToTheEnd(new AteOutcome.Read(Reading()));
        var undo = new AteUndo();
        undo.Begin(new NoticeBoard(), recorded!, null, Evening);

        Assert.False(undo.Expire(Evening));
        Assert.True(undo.Expire(Evening + NoticeBoard.UndoWindow + TimeSpan.FromMilliseconds(1)));
        Assert.Null(undo.Pending);
        Assert.Null(undo.Reverse(AteUndo.ActKey));
    }

    [Fact]
    public void AnotherActsKeyIsNotOurs()
    {
        var undo = new AteUndo();
        undo.Begin(new NoticeBoard(), Estimate(), null, Evening);

        Assert.Null(undo.Reverse("something-else"));
        Assert.NotNull(undo.Pending);
    }
}
