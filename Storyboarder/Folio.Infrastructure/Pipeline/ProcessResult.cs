namespace Folio.Infrastructure.Pipeline;

public enum ProcessOutcome
{
    DidWork,   // advanced the pipeline; poll again immediately
    Idle,      // nothing to do (no active novel, or blocked on a failed unit)
    Waiting    // a due time / rate-limit window must elapse before more work is possible
}

public readonly record struct ProcessResult(ProcessOutcome Outcome, TimeSpan Wait, Guid? NovelId)
{
    public static ProcessResult DidWork(Guid novelId) => new(ProcessOutcome.DidWork, TimeSpan.Zero, novelId);
    public static readonly ProcessResult Idle = new(ProcessOutcome.Idle, TimeSpan.Zero, null);
    public static ProcessResult Waiting(TimeSpan wait, Guid novelId) =>
        new(ProcessOutcome.Waiting, wait < TimeSpan.Zero ? TimeSpan.Zero : wait, novelId);
}
