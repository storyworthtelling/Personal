using Constitution.Domain.Workouts;

namespace Constitution.Domain.Meals;

/// <summary>What a meal author hands back: a week, or the plain fact that it has none. Reasons reuse the workouts' closed list.</summary>
public abstract record MealAuthorResult
{
    public sealed record Authored(MealWeek Week) : MealAuthorResult;

    public sealed record Unavailable(AuthorUnavailable Reason) : MealAuthorResult;
}

/// <summary>
/// The seam where a Claude-authored week plugs in later (R10), mirroring <see cref="IPlanAuthor"/>. An implementation lives in
/// Infrastructure, does the I/O, and turns every failure it can name into <see cref="MealAuthorResult.Unavailable"/>; an exception
/// it does not understand is a bug and propagates.
/// </summary>
public interface IMealAuthor
{
    Task<MealAuthorResult> AuthorAsync(MealWeekRequest request, CancellationToken cancellation);
}

/// <summary>The author used until there is one: every week is the rules week, and the page cannot tell.</summary>
public sealed class NoMealAuthor : IMealAuthor
{
    public Task<MealAuthorResult> AuthorAsync(MealWeekRequest request, CancellationToken cancellation) =>
        Task.FromResult<MealAuthorResult>(new MealAuthorResult.Unavailable(AuthorUnavailable.NotConfigured));
}

/// <summary>The one place the origin is written down (Diagnostics). It never reaches a page, a notice or a voiced line.</summary>
public interface IMealLog
{
    void Record(PlanOrigin origin, AuthorUnavailable? reason);
}

public sealed record MealOutcome(MealWeek Week, PlanOrigin Origin, AuthorUnavailable? Reason);

/// <summary>
/// Asks the preferred author for the week and holds what it says to <see cref="MealRules"/>; if there is no week, or it breaks a
/// rule, the rules planner's week is used instead (C-6.3.3). When the rules say nothing may be planned (a calendar unreadable, the
/// box short of ten), the author is not asked: failing closed is not something a better author gets to overrule.
/// </summary>
public sealed class MealWithFallback
{
    private readonly IMealAuthor preferred;
    private readonly IMealLog log;

    public MealWithFallback(IMealAuthor preferred, IMealLog log)
    {
        this.preferred = preferred ?? throw new ArgumentNullException(nameof(preferred));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<MealOutcome> PlanAsync(MealWeekRequest request, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        if (MealPlanner.Stop(request) is not null)
        {
            var stopped = new MealOutcome(MealPlanner.Plan(request), PlanOrigin.RulesOnly, null);
            log.Record(stopped.Origin, stopped.Reason);
            return stopped;
        }

        var result = await preferred.AuthorAsync(request, cancellation);
        var reason = result switch
        {
            MealAuthorResult.Authored authored when authored.Week.IsPlanned && MealRules.Violations(request, authored.Week).Count == 0 => (AuthorUnavailable?)null,
            MealAuthorResult.Authored => AuthorUnavailable.BrokeTheRules,
            MealAuthorResult.Unavailable unavailable => unavailable.Reason,
            _ => throw new InvalidOperationException("The meal author returned neither a week nor a reason."),
        };

        var outcome = reason is null
            ? new MealOutcome(((MealAuthorResult.Authored)result).Week, PlanOrigin.Authored, null)
            : new MealOutcome(MealPlanner.Plan(request), PlanOrigin.RulesOnly, reason);
        log.Record(outcome.Origin, outcome.Reason);
        return outcome;
    }
}
