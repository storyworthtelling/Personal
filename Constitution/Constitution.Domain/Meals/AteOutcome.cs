namespace Constitution.Domain.Meals;

/// <summary>What asking for an estimate came to. Only <see cref="Read"/> is ever recorded; the rest are sentences on the card.</summary>
public abstract record AteOutcome
{
    /// <summary>A usable reading.</summary>
    public sealed record Read(AteReading Reading) : AteOutcome;

    /// <summary>The photograph held no food or drink.</summary>
    public sealed record NoMeal : AteOutcome;

    /// <summary>The reply, or the photograph, could not be made sense of. This is "could not estimate", never a figure.</summary>
    public sealed record Unreadable : AteOutcome;

    /// <summary>No estimate could be asked for (no key, no route); the sentence says why.</summary>
    public sealed record Unavailable(string Sentence) : AteOutcome;
}

/// <summary>
/// Reads one photograph into an outcome. The path is the only thing handed over; the implementation reads the file itself and holds
/// nothing of it afterwards. A transport failure is allowed to throw; the model turns it into a sentence.
/// </summary>
public interface IAteEstimator
{
    Task<AteOutcome> EstimateAsync(string photoPath, CancellationToken cancellation);
}

/// <summary>The estimator used until a key is set: it says so and guesses nothing.</summary>
public sealed class UnavailableAteEstimator : IAteEstimator
{
    public Task<AteOutcome> EstimateAsync(string photoPath, CancellationToken cancellation) =>
        Task.FromResult<AteOutcome>(new AteOutcome.Unavailable(AteText.NoKeySentence));
}
