using Constitution.Domain.Workouts.Pipeline;

namespace Constitution.Domain.Workouts.Screens;

/// <summary>
/// The Workouts tab's plan source, with the owner's hand kept: every swap, move and skip the tab records is also put in the
/// <see cref="IWorkoutOverrides"/> the weekly run reads, so the run plans the week the owner left and not the one it would have made.
/// The override goes first: it is the owner's word, and the one thing the run cannot work out again. If it cannot be kept the
/// change is not recorded at all, so the tab and the run never disagree about what was done.
/// </summary>
public sealed class OverridingWorkoutsSource(IWorkoutsSource inner, IWorkoutOverrides overrides) : IWorkoutsSource
{
    private readonly IWorkoutsSource inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IWorkoutOverrides overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));

    public WorkoutsRead Read(DateTimeOffset now) => inner.Read(now);

    public void Record(WorkoutsChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        overrides.Add(PlanOverrides.From(change));
        inner.Record(change);
    }
}
