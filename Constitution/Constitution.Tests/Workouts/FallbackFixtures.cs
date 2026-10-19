using Constitution.Domain.Workouts;

namespace Constitution.Tests.Workouts;

internal sealed class RecordingLog : IPlanLog
{
    public List<(PlanOrigin Origin, AuthorUnavailable? Reason)> Entries { get; } = [];

    public void Record(PlanOrigin origin, AuthorUnavailable? reason) => Entries.Add((origin, reason));
}

/// <summary>An author that answers with what it was given, and notes who it was asked about.</summary>
internal sealed class ScriptedAuthor : IPlanAuthor
{
    private readonly PlanAuthorResult answer;

    public ScriptedAuthor(PlanAuthorResult answer)
    {
        this.answer = answer;
    }

    public int Calls { get; private set; }

    public CancellationToken LastToken { get; private set; }

    public Task<PlanAuthorResult> AuthorAsync(WorkoutPlanRequest request, CancellationToken cancellation)
    {
        Calls++;
        LastToken = cancellation;
        return Task.FromResult(answer);
    }
}

/// <summary>An author that is never reachable and counts how often it was tried.</summary>
internal sealed class CountingAuthor : IPlanAuthor
{
    public int Calls { get; private set; }

    public IReadOnlyList<PersonWeek> Asked { get; private set; } = [];

    public Task<PlanAuthorResult> AuthorAsync(WorkoutPlanRequest request, CancellationToken cancellation)
    {
        Calls++;
        Asked = request.People;
        return Task.FromResult<PlanAuthorResult>(new PlanAuthorResult.Unavailable(AuthorUnavailable.Offline));
    }
}

internal sealed class ThrowingAuthor : IPlanAuthor
{
    public Task<PlanAuthorResult> AuthorAsync(WorkoutPlanRequest request, CancellationToken cancellation) =>
        throw new InvalidOperationException("The author broke.");
}

internal static class Fallback
{
    public static PlanWithFallback Offline() =>
        new(new ScriptedAuthor(new PlanAuthorResult.Unavailable(AuthorUnavailable.Offline)), new RecordingLog());
}
