using Constitution.Domain.Delivery;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Meals;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.MealWorker;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWorker.MealWorkerData;

namespace Constitution.Tests.RecipeHour;

/// <summary>The pass sends the week in progress its recipes when they are in their hour, and only then. It changes no stamp: the delivery record is what says it is done.</summary>
public class RecipeHourPassTests
{
    private static readonly DateTimeOffset Inside = Edt(10, 26, 16, 20);

    private static PassRig Quiet() => new()
    {
        StampRead = Stamp(Edt(10, 26, 6, 10), NextSunday),
        Weeks = Kept(Sunday),
        Decisions = Decided(Sunday),
        Person = Sam,
    };

    private static Task<GateResult<MealRecipesOutcome>> Says(MealRecipesOutcome outcome) =>
        Task.FromResult<GateResult<MealRecipesOutcome>>(new GateResult<MealRecipesOutcome>.Ran(outcome));

    [Fact]
    public async Task ARecipeInItsHourIsAskedForAndNothingElse()
    {
        var rig = Quiet();

        var result = await rig.RunAsync(Inside);

        Assert.Equal(["read stamp", "read weeks", "read decisions", "read delivered", "recipes"], rig.Diary);
        Assert.IsType<MealPassStep<MealRecipesOutcome>.Done>(result.Recipes);
        Assert.IsType<MealPassStep<MealWeekOutcome>.Skipped>(result.Plan);
        Assert.IsType<MealPassStep<MealDeadlineOutcome>.Skipped>(result.Deadline);
        Assert.False(result.NothingRan);
    }

    [Fact]
    public async Task TheRecipeHourLeavesTheStampAlone()
    {
        var rig = Quiet();

        await rig.RunAsync(Inside);

        Assert.Empty(rig.Saved);
        Assert.Empty(rig.Lines);
    }

    [Fact]
    public async Task OutsideTheHourTheRecordOfDeliveriesIsNotEvenRead()
    {
        var rig = Quiet();

        var result = await rig.RunAsync(Edt(10, 26, 9, 5));

        Assert.True(result.NothingRan);
        Assert.Equal(["read stamp", "read weeks", "read decisions"], rig.Diary);
    }

    [Fact]
    public async Task ADeliveredPageCostsOnlyTheLocalReads()
    {
        var rig = Quiet();
        var name = DeliveredName.For(DeliveryKind.Recipe, Sunday.AddDays(1), Sam);
        rig.DeliveredRecords[name] = new DeliveredMealDocument(name, DeliveryKind.Recipe, Sam, Sunday.AddDays(1), Edt(10, 26, 17, 15), "same");

        var result = await rig.RunAsync(Inside);

        Assert.True(result.NothingRan);
        Assert.DoesNotContain("recipes", rig.Diary);
    }

    [Fact]
    public async Task ABusyGateIsTypedAndLoggedAndTheNextPassTriesAgain()
    {
        var rig = Quiet();
        rig.Recipes = _ => Task.FromResult<GateResult<MealRecipesOutcome>>(new GateResult<MealRecipesOutcome>.Busy("Another run has the calendars."));

        var result = await rig.RunAsync(Inside);

        Assert.IsType<MealPassStep<MealRecipesOutcome>.Busy>(result.Recipes);
        Assert.Equal([MealPassTrail.LogPhrase(MealPassPart.Recipes, MealPassProblem.Busy, string.Empty)], rig.Lines);
        Assert.Empty(rig.Saved);
        result.ThrowIfFaulted();
    }

    [Fact]
    public async Task AThrownFailureIsKeptWholeAndLoggedByTypeNameOnly()
    {
        var rig = Quiet();
        rig.Recipes = _ => throw new InvalidOperationException("secret words in the message");

        var result = await rig.RunAsync(Inside);

        var faulted = Assert.IsType<MealPassStep<MealRecipesOutcome>.Faulted>(result.Recipes);
        Assert.IsType<InvalidOperationException>(faulted.Failure);
        Assert.Equal([MealPassTrail.LogPhrase(MealPassPart.Recipes, MealPassProblem.Failed, nameof(InvalidOperationException))], rig.Lines);
        Assert.Throws<InvalidOperationException>(result.ThrowIfFaulted);
    }

    [Fact]
    public async Task ARecipeFailureDoesNotStopThePlanOrTheDeadline()
    {
        var rig = new PassRig { Person = Sam, Weeks = Kept(Sunday) };
        rig.Recipes = _ => throw new InvalidOperationException("boom");

        var result = await rig.RunAsync(Edt(10, 26, 16, 20));

        Assert.Contains("plan", rig.Diary);
        Assert.Contains("decide", rig.Diary);
        Assert.IsType<MealPassStep<MealRecipesOutcome>.Faulted>(result.Recipes);
    }

    [Fact]
    public async Task ARecordOfDeliveriesThatWillNotOpenStillStartsTheRunWhichReportsIt()
    {
        var rig = Quiet();
        rig.DeliveredFails = new IOException("The record is locked.");

        var result = await rig.RunAsync(Inside);

        Assert.Contains("recipes", rig.Diary);
        Assert.IsType<MealPassStep<MealRecipesOutcome>.Done>(result.Recipes);
    }

    [Fact]
    public async Task TheRecipesGoFirstBecauseTheyAreTheOnlyPartWithAClockOnIt()
    {
        var rig = new PassRig { Person = Sam, Weeks = Kept(Sunday) };

        await rig.RunAsync(Inside);

        Assert.True(rig.Diary.IndexOf("recipes") < rig.Diary.IndexOf("plan"));
        Assert.True(rig.Diary.IndexOf("recipes") < rig.Diary.IndexOf("decide"));
    }

    [Fact]
    public async Task ACallersCancellationIsNotAFault()
    {
        var rig = Quiet();
        using var source = new CancellationTokenSource();
        rig.Recipes = token =>
        {
            source.Cancel();
            token.ThrowIfCancellationRequested();
            return Says(new MealRecipesOutcome.Nothing("None."));
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.RunAsync(Inside, cancellation: source.Token));
    }

    [Fact]
    public async Task TwoFailuresComeBackTogether()
    {
        var rig = Quiet();
        rig.Recipes = _ => throw new IOException("one");
        rig.SaveFails = null;
        rig.Weeks = Kept(Sunday);
        rig.StampRead = new MealPassStampRead.NothingYet();
        rig.Plan = _ => throw new InvalidOperationException("two");

        var result = await rig.RunAsync(Inside);

        var all = Assert.Throws<AggregateException>(result.ThrowIfFaulted);
        Assert.Equal(2, all.InnerExceptions.Count);
    }
}
