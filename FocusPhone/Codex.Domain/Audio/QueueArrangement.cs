namespace Codex.Domain.Audio;

/// <summary>
/// The strategies the user can pick to auto-arrange the podcast queue from the
/// pool of unplayed episodes. <see cref="Manual"/> means the user has ordered
/// the queue by hand and no strategy is applied.
/// </summary>
public enum QueueArrangement
{
    Manual,
    RoundRobin,
    Chronological,
    BacklogBuster,
}
