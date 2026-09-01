namespace Storyboarder.Domain.Model;

// A unit of work the background worker can retry with backoff. Implemented by the entities whose
// processing calls a rate-limited external service (chunk extraction, reference sheets, panels).
public interface IRetryable
{
    int Attempts { get; set; }
    DateTimeOffset? NextAttemptAtUtc { get; set; }
    string? LastError { get; set; }

    // The work status as the retry machinery sees it (maps to each entity's own status field).
    WorkStatus WorkState { get; set; }
}
