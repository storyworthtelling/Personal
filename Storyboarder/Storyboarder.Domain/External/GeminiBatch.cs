namespace Storyboarder.Domain.External;

// One panel to render in a batch: the same inputs a synchronous GeneratePanel takes, plus the PanelId so
// the result can be matched back to the row. References are the design-sheet image bytes; the batch client
// is responsible for uploading them once and referencing them (they can't be inlined per request at scale).
public sealed record BatchPanelRequest(
    Guid PanelId,
    string VisualPrompt,
    string ArtStyle,
    IReadOnlyList<byte[]> References,
    double? AspectRatio);

// The outcome for one panel once a batch job finishes. A render either produced an image or it didn't; the
// pipeline deliberately does NOT distinguish a content block from a transient error here, because either
// way the panel is sent back through the ordinary synchronous render path (which re-surfaces a real block
// to HandleBlockedPanel, and may simply succeed on a nondeterministic retry). Error is for logging only.
public sealed record BatchPanelOutcome(Guid PanelId, byte[]? Image, string? Error)
{
    public bool Succeeded => Image is not null;
}

public enum BatchJobState
{
    // The job is still being processed server-side; poll again later.
    Running,
    // The job finished; Results carries a per-panel outcome (some of which may be failures).
    Succeeded,
    // The whole job failed (auth, malformed submission, expired). Every panel in it falls back to the
    // synchronous render path.
    Failed
}

public sealed record BatchJobStatus(BatchJobState State, IReadOnlyList<BatchPanelOutcome> Results)
{
    public static BatchJobStatus StillRunning { get; } = new(BatchJobState.Running, []);
    public static BatchJobStatus Broken { get; } = new(BatchJobState.Failed, []);
}

// Renders panels through the Gemini Batch API: a first pass over the bulk of a book's panels at roughly
// half the synchronous price and on a separate quota pool, at the cost of an up-to-24h turnaround. Only
// the FIRST render attempt goes through here; anything that fails or is blocked is handled synchronously
// afterward (see GraphicNovelProcessor and BATCHING.md), so the block-handling flow is unchanged.
public interface IGeminiBatchClient
{
    // Submits the requests as one job and returns its id. The id is persisted so the worker can poll the
    // job across restarts.
    Task<string> SubmitAsync(IReadOnlyList<BatchPanelRequest> requests, CancellationToken cancellationToken);

    // Reports whether the job is still running, finished (with per-panel results), or failed outright.
    Task<BatchJobStatus> PollAsync(string jobId, CancellationToken cancellationToken);
}
