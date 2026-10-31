using System.Runtime.ExceptionServices;
using Constitution.Domain.Calendar;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Workouts;

namespace Constitution.Infrastructure.Meals;

/// <summary>What became of one part of the pass.</summary>
public abstract record MealPassStep<T>
{
    private MealPassStep()
    {
    }

    /// <summary>Nothing was due, so nothing was asked of the calendars, the household file or the tablet.</summary>
    public sealed record Skipped : MealPassStep<T>;

    public sealed record Done(T Outcome) : MealPassStep<T>;

    /// <summary>Another run held the calendars for the whole wait; the part never started and wrote nothing.</summary>
    public sealed record Busy(string Sentence) : MealPassStep<T>;

    /// <summary>The part threw. The exception is kept whole, stack and all, for <see cref="MealPassResult.ThrowIfFaulted"/>.</summary>
    public sealed record Faulted(Exception Failure) : MealPassStep<T>;
}

/// <summary>
/// Everything the pass reaches outside itself. <paramref name="Delivered"/> and <paramref name="TabletPerson"/> are what the recipe-hour look reads (the record of what has
/// gone to the tablet, read only while an hour is open, and the adult whose tablet this is; null when none); <paramref name="Recipes"/> is the documents-only run (D120), which also serves the
/// weeks the lease holder published (D127). <paramref name="SharedPlans"/> are those weeks, read once per pass from this phone's copy of the household file (a local read, no network); it never
/// throws, and null (the older callers) means none are read, so nothing shared is ever due.
/// </summary>
public sealed record MealPassSeams(
    IMealWeekRecords Weeks,
    IMealDecisionRecords Decisions,
    IMealPassStamps Stamps,
    Func<CancellationToken, Task<GateResult<MealWeekOutcome>>> PlanWeek,
    Func<CancellationToken, Task<GateResult<MealDeadlineOutcome>>> Decide,
    IWorkoutWeekLog Log,
    IDeliveredMealDocuments Delivered,
    string? TabletPerson,
    Func<CancellationToken, Task<GateResult<MealRecipesOutcome>>> Recipes,
    Func<IReadOnlyList<SharedWeekPlan>>? SharedPlans = null);

/// <summary>What one pass did. <see cref="Recipes"/> is the recipe-hour part (D120).</summary>
public sealed record MealPassResult(
    MealPassStep<MealWeekOutcome> Plan, MealPassStep<MealDeadlineOutcome> Deadline, MealPassStep<MealRecipesOutcome> Recipes, Exception? StampFailure)
{
    public bool NothingRan =>
        Plan is MealPassStep<MealWeekOutcome>.Skipped
        && Deadline is MealPassStep<MealDeadlineOutcome>.Skipped
        && Recipes is MealPassStep<MealRecipesOutcome>.Skipped;

    /// <summary>
    /// Rethrows what the pass kept: one failure as itself, with the stack it was thrown with, and several together. The parts do not stop each other, so a
    /// caller that wants the pass to count as failed (the worker) asks for this after the pass has done everything it could.
    /// </summary>
    public void ThrowIfFaulted()
    {
        var faults = new List<Exception>();
        if (Plan is MealPassStep<MealWeekOutcome>.Faulted plan)
        {
            faults.Add(plan.Failure);
        }

        if (Deadline is MealPassStep<MealDeadlineOutcome>.Faulted deadline)
        {
            faults.Add(deadline.Failure);
        }

        if (Recipes is MealPassStep<MealRecipesOutcome>.Faulted recipes)
        {
            faults.Add(recipes.Failure);
        }

        if (StampFailure is not null)
        {
            faults.Add(StampFailure);
        }

        if (faults.Count == 1)
        {
            ExceptionDispatchInfo.Capture(faults[0]).Throw();
        }

        if (faults.Count > 1)
        {
            throw new AggregateException("The meal pass had more than one failure.", faults);
        }
    }
}

/// <summary>
/// The meal pass: what the hourly worker (and the app opening) does for the meal side. It first looks at three small local files (the stamp of when it last
/// looked, the kept weeks, the decided weeks) and asks <see cref="MealPassPlan"/> what is due. <b>When nothing is due it stops there:</b> no household file, no calendar,
/// no tablet, no network and no trail line. When something is due it runs, in this order, the recipes of the week in progress (<see cref="MealPassSeams.Recipes"/>, only while one is in its
/// hour), the weekly meal run
/// (<see cref="MealPassSeams.PlanWeek"/>, which plans the coming week, places it and puts the sheet on the tablet) and then the deadline step
/// (<see cref="MealPassSeams.Decide"/>), each through the shared run gate by the seam it is given.
///
/// <para><b>Each part stands alone.</b> A part that finds the gate busy never started and is retried by the next pass; a part that throws is kept whole in the
/// result (<see cref="MealPassStep{T}.Faulted"/>), logged by type name only, and the other part still runs. The caller asks <see cref="MealPassResult.ThrowIfFaulted"/>
/// once both have had their chance, so nothing is swallowed and the stack survives. A caller's own cancellation is not a fault and propagates at once.</para>
///
/// <para><b>The stamp.</b> After a part runs the pass records it: a run that planned, or found this phone is not the planner, settles the plan for the day and the
/// week; any other plan answer (not set up, recipes unreadable, calendars unreadable) is only <i>tried</i>, so a scheduled pass waits three hours and opening the
/// app tries at once. The deadline is always tried, never settled: the decided weeks themselves say whether it is done.
/// A stamp that cannot be written is typed (<see cref="MealPassResult.StampFailure"/>) and logged; the work it describes stands.</para>
///
/// <para>The runs write their own trail lines, so the pass writes only its own problems (<see cref="MealPassTrail"/>).</para>
/// </summary>
public static class MealWeekPass
{
    public static async Task<MealPassResult> RunAsync(MealPassTrigger trigger, DateTimeOffset now, TimeZoneInfo zone, MealPassSeams seams, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(seams);
        cancellation.ThrowIfCancellationRequested();

        var read = seams.Stamps.Load();
        var work = MealPassPlan.Decide(
            trigger, now, zone, read, seams.Weeks.Load(), seams.Decisions.Load(), seams.TabletPerson, Lookup(seams.Delivered), seams.SharedPlans?.Invoke());
        var stamp = (read as MealPassStampRead.Held)?.Stamp ?? MealPassStamp.Empty;
        MealPassStep<MealWeekOutcome> plan = new MealPassStep<MealWeekOutcome>.Skipped();
        MealPassStep<MealDeadlineOutcome> deadline = new MealPassStep<MealDeadlineOutcome>.Skipped();
        MealPassStep<MealRecipesOutcome> recipes = new MealPassStep<MealRecipesOutcome>.Skipped();
        Exception? stampFailure = null;

        // First, because it is the one part with a clock on it: a recipe has an hour, and a plan or a deadline that waits on the gate must not spend it.
        // It keeps no stamp: the record of what has gone to the tablet is what says it is done, and the next pass reads that.
        if (work.RecipeHour || work.SharedWeek)
        {
            recipes = await AttemptAsync(seams.Recipes, MealPassPart.Recipes, seams.Log, cancellation);
        }

        if (work.PlanWeek)
        {
            plan = await AttemptAsync(seams.PlanWeek, MealPassPart.Plan, seams.Log, cancellation);
            if (plan is not MealPassStep<MealWeekOutcome>.Busy)
            {
                var settled = plan is MealPassStep<MealWeekOutcome>.Done done && MealPassPlan.Settles(done.Outcome);
                stamp = settled
                    ? stamp with { PlanSettledAt = now, PlanWeek = PlanningWeek.StartOf(now, zone).AddDays(PlanningWeek.DaysInWeek), PlanTriedAt = now }
                    : stamp with { PlanTriedAt = now };
                stampFailure ??= Remember(seams, stamp, MealPassPart.Plan);
            }
        }

        if (work.Deadline)
        {
            deadline = await AttemptAsync(seams.Decide, MealPassPart.Deadline, seams.Log, cancellation);
            if (deadline is not MealPassStep<MealDeadlineOutcome>.Busy)
            {
                stamp = stamp with { DeadlineTriedAt = now };
                stampFailure ??= Remember(seams, stamp, MealPassPart.Deadline);
            }
        }

        return new MealPassResult(plan, deadline, recipes, stampFailure);
    }

    // A record of deliveries that cannot be opened right now is not read as "delivered" and not as a fault of the look: the page counts as waiting, so the recipes run is
    // started, reads the same record itself and reports the failure with its stack (a fault of the part, logged by type name). Nothing is hidden, and a page is never skipped.
    private static Func<string, DeliveredMealDocument?> Lookup(IDeliveredMealDocuments delivered) => name =>
    {
        try
        {
            return delivered.Find(name);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    };

    private static async Task<MealPassStep<T>> AttemptAsync<T>(
        Func<CancellationToken, Task<GateResult<T>>> run, MealPassPart part, IWorkoutWeekLog log, CancellationToken cancellation)
    {
        try
        {
            var turn = await run(cancellation);
            if (turn is GateResult<T>.Ran ran)
            {
                return new MealPassStep<T>.Done(ran.Outcome);
            }

            if (turn is GateResult<T>.Busy busy)
            {
                log.Outcome(MealPassTrail.LogPhrase(part, MealPassProblem.Busy, string.Empty));
                return new MealPassStep<T>.Busy(busy.Sentence);
            }

            throw new InvalidOperationException("The run gate gave an answer the meal pass does not know.");
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            // Kept whole in the result and rethrown by ThrowIfFaulted, so the other part still gets its turn; the trail gets the type name and nothing the failure said.
            log.Outcome(MealPassTrail.LogPhrase(part, MealPassProblem.Failed, failure.GetType().Name));
            return new MealPassStep<T>.Faulted(failure);
        }
    }

    private static Exception? Remember(MealPassSeams seams, MealPassStamp stamp, MealPassPart part)
    {
        try
        {
            seams.Stamps.Save(stamp);
            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            seams.Log.Outcome(MealPassTrail.LogPhrase(part, MealPassProblem.StampNotSaved, failure.GetType().Name));
            return failure;
        }
    }
}
