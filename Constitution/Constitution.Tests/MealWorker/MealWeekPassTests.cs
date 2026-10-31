using System.Runtime.CompilerServices;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Meals;
using Constitution.Infrastructure.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWorker.MealWorkerData;

namespace Constitution.Tests.MealWorker;

/// <summary>The pass: what it asks of whom, in what order, and what it leaves behind when nothing is due, something is busy or something throws.</summary>
public class MealWeekPassTests
{
    private static readonly DateTimeOffset Morning = Edt(10, 26, 7, 5);

    private static Task<GateResult<MealWeekOutcome>> PlanSays(MealWeekOutcome outcome) =>
        Task.FromResult<GateResult<MealWeekOutcome>>(new GateResult<MealWeekOutcome>.Ran(outcome));

    // ---- nothing due ----

    [Fact]
    public async Task WhenNothingIsDueNothingIsAskedOfAnyRun()
    {
        var rig = new PassRig { StampRead = Stamp(Edt(10, 26, 6, 10), NextSunday) };

        var result = await rig.RunAsync(Morning);

        Assert.True(result.NothingRan);
        Assert.Equal(["read stamp", "read weeks", "read decisions"], rig.Diary);
        Assert.Empty(rig.Saved);
        Assert.Empty(rig.Lines);
    }

    [Fact]
    public async Task ADecidedWeekCostsOnlyTheLocalReads()
    {
        var rig = new PassRig { StampRead = Stamp(Edt(10, 26, 6, 10), NextSunday), Weeks = Kept(Sunday), Decisions = Decided(Sunday) };

        var result = await rig.RunAsync(Morning);

        Assert.True(result.NothingRan);
        Assert.DoesNotContain("plan", rig.Diary);
        Assert.DoesNotContain("decide", rig.Diary);
    }

    // ---- the order and the stamp ----

    [Fact]
    public async Task ThePlanRunsBeforeTheDeadlineAndEachIsRemembered()
    {
        var rig = new PassRig().BothDue();

        var result = await rig.RunAsync(Morning);

        Assert.Equal(["read stamp", "read weeks", "read decisions", "plan", "save stamp", "decide", "save stamp"], rig.Diary);
        Assert.IsType<MealPassStep<MealWeekOutcome>.Done>(result.Plan);
        Assert.IsType<MealPassStep<MealDeadlineOutcome>.Done>(result.Deadline);
        var last = rig.Saved[^1];
        Assert.Equal(Morning, last.PlanSettledAt);
        Assert.Equal(NextSunday, last.PlanWeek);
        Assert.Equal(Morning, last.DeadlineTriedAt);
    }

    [Fact]
    public async Task AnOutcomeTheHouseholdCanMendIsTriedButNotSettled()
    {
        var rig = new PassRig { Plan = _ => PlanSays(new MealWeekOutcome.NotPlanned(MealNotPlannedBecause.NotConfigured, "No setup.")) };

        await rig.RunAsync(Morning);

        var stamp = Assert.Single(rig.Saved);
        Assert.Null(stamp.PlanSettledAt);
        Assert.Equal(Morning, stamp.PlanTriedAt);
    }

    [Fact]
    public async Task NotThePlannerSettlesSoThePartnersPhoneStaysQuiet()
    {
        var rig = new PassRig { Plan = _ => PlanSays(new MealWeekOutcome.NotPlanned(MealNotPlannedBecause.NotThePlanner, "The other phone plans.")) };

        await rig.RunAsync(Morning);
        var again = await rig.RunAsync(Morning.AddHours(1));

        Assert.Equal(Morning, Assert.Single(rig.Saved).PlanSettledAt);
        Assert.True(again.NothingRan);
    }

    [Fact]
    public async Task TheDeadlineStampKeepsWhatThePlanStampSaid()
    {
        var rig = new PassRig().BothDue();

        await rig.RunAsync(Morning);

        Assert.Equal(2, rig.Saved.Count);
        Assert.Equal(NextSunday, rig.Saved[1].PlanWeek);
        Assert.Equal(Morning, rig.Saved[1].PlanSettledAt);
    }

    [Fact]
    public async Task OnlyTheDeadlineRunsWhenOnlyItIsDue()
    {
        var rig = new PassRig { StampRead = Stamp(Edt(10, 26, 6, 10), NextSunday), Weeks = Kept(Sunday) };

        var result = await rig.RunAsync(Morning);

        Assert.IsType<MealPassStep<MealWeekOutcome>.Skipped>(result.Plan);
        Assert.IsType<MealPassStep<MealDeadlineOutcome>.Done>(result.Deadline);
        Assert.DoesNotContain("plan", rig.Diary);
    }

    // ---- busy ----

    [Fact]
    public async Task ABusyGateIsTypedLoggedAndNotRemembered()
    {
        var rig = new PassRig { Plan = _ => Task.FromResult<GateResult<MealWeekOutcome>>(new GateResult<MealWeekOutcome>.Busy("Another run was placing events.")) };

        var result = await rig.RunAsync(Morning);

        var busy = Assert.IsType<MealPassStep<MealWeekOutcome>.Busy>(result.Plan);
        Assert.Equal("Another run was placing events.", busy.Sentence);
        Assert.Equal([MealPassTrail.LogPhrase(MealPassPart.Plan, MealPassProblem.Busy, string.Empty)], rig.Lines);
        Assert.Empty(rig.Saved);
    }

    [Fact]
    public async Task ABusyPlanDoesNotStopTheDeadline()
    {
        var rig = new PassRig { Plan = _ => Task.FromResult<GateResult<MealWeekOutcome>>(new GateResult<MealWeekOutcome>.Busy("Busy.")) }.BothDue();

        var result = await rig.RunAsync(Morning);

        Assert.IsType<MealPassStep<MealDeadlineOutcome>.Done>(result.Deadline);
    }

    [Fact]
    public async Task ABusyDeadlineIsTypedAndLogged()
    {
        var rig = new PassRig
        {
            StampRead = Stamp(Edt(10, 26, 6, 10), NextSunday),
            Weeks = Kept(Sunday),
            Decide = _ => Task.FromResult<GateResult<MealDeadlineOutcome>>(new GateResult<MealDeadlineOutcome>.Busy("Busy.")),
        };

        var result = await rig.RunAsync(Morning);

        Assert.IsType<MealPassStep<MealDeadlineOutcome>.Busy>(result.Deadline);
        Assert.Equal([MealPassTrail.LogPhrase(MealPassPart.Deadline, MealPassProblem.Busy, string.Empty)], rig.Lines);
        Assert.Empty(rig.Saved);
    }

    // ---- faults ----

    [Fact]
    public async Task AThrowingPlanIsKeptWholeLoggedByTypeAndTheDeadlineStillRuns()
    {
        var failure = new InvalidOperationException("an invented fault");
        var rig = new PassRig { Plan = _ => throw failure }.BothDue();

        var result = await rig.RunAsync(Morning);

        Assert.Same(failure, Assert.IsType<MealPassStep<MealWeekOutcome>.Faulted>(result.Plan).Failure);
        Assert.IsType<MealPassStep<MealDeadlineOutcome>.Done>(result.Deadline);
        Assert.Contains(MealPassTrail.LogPhrase(MealPassPart.Plan, MealPassProblem.Failed, nameof(InvalidOperationException)), rig.Lines);
        Assert.DoesNotContain(rig.Lines, line => line.Contains("invented fault", StringComparison.Ordinal));
        Assert.Null(rig.Saved[0].PlanSettledAt);
        Assert.Equal(Morning, rig.Saved[0].PlanTriedAt);
    }

    [Fact]
    public async Task ThrowIfFaultedRethrowsTheSameExceptionWithItsStack()
    {
        var rig = new PassRig { Plan = _ => ThrowFromAMethod() };

        var result = await rig.RunAsync(Morning);
        var thrown = Assert.Throws<InvalidOperationException>(result.ThrowIfFaulted);

        Assert.Contains(nameof(ThrowFromAMethod), thrown.StackTrace, StringComparison.Ordinal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<GateResult<MealWeekOutcome>> ThrowFromAMethod() => throw new InvalidOperationException("an invented fault");

    [Fact]
    public async Task TwoFaultsComeBackTogether()
    {
        var rig = new PassRig { Plan = _ => throw new InvalidOperationException("one"), Decide = _ => throw new IOException("two") }.BothDue();

        var result = await rig.RunAsync(Morning);
        var thrown = Assert.Throws<AggregateException>(result.ThrowIfFaulted);

        Assert.Equal(2, thrown.InnerExceptions.Count);
    }

    [Fact]
    public async Task ACleanPassThrowsNothing()
    {
        var rig = new PassRig().BothDue();

        var result = await rig.RunAsync(Morning);

        result.ThrowIfFaulted();
    }

    [Fact]
    public async Task ACancelledRunIsNotAFault()
    {
        using var source = new CancellationTokenSource();
        var rig = new PassRig { Plan = token => Task.FromCanceled<GateResult<MealWeekOutcome>>(CancelledToken(source)) }.BothDue();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.RunAsync(Morning, cancellation: source.Token));
        Assert.DoesNotContain("decide", rig.Diary);
    }

    private static CancellationToken CancelledToken(CancellationTokenSource source)
    {
        source.Cancel();
        return source.Token;
    }

    [Fact]
    public async Task AStampThatCannotBeSavedIsTypedAndLoggedAndTheWorkStands()
    {
        var rig = new PassRig { SaveFails = new IOException("disk full") }.BothDue();

        var result = await rig.RunAsync(Morning);

        Assert.IsType<IOException>(result.StampFailure);
        Assert.IsType<MealPassStep<MealDeadlineOutcome>.Done>(result.Deadline);
        Assert.Contains(MealPassTrail.LogPhrase(MealPassPart.Plan, MealPassProblem.StampNotSaved, nameof(IOException)), rig.Lines);
        Assert.Throws<IOException>(result.ThrowIfFaulted);
    }

    [Fact]
    public async Task AnUnreadableStampOrRecordStillLetsThePassAskTheRunsToSayWhy()
    {
        var rig = new PassRig
        {
            StampRead = new MealPassStampRead.Unreadable("could not be opened"),
            Weeks = new MealWeekRecordRead.Unreadable("could not be opened"),
        };

        var result = await rig.RunAsync(Morning);

        Assert.IsType<MealPassStep<MealWeekOutcome>.Done>(result.Plan);
        Assert.IsType<MealPassStep<MealDeadlineOutcome>.Done>(result.Deadline);
    }

    [Fact]
    public async Task ArgumentsAreChecked()
    {
        var rig = new PassRig();

        await Assert.ThrowsAsync<ArgumentNullException>(() => MealWeekPass.RunAsync(MealPassTrigger.Scheduled, Morning, null!, rig.Seams, default));
        await Assert.ThrowsAsync<ArgumentNullException>(() => MealWeekPass.RunAsync(MealPassTrigger.Scheduled, Morning, Zone, null!, default));
    }
}
