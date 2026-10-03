namespace Index.Infrastructure.Pipeline;

public enum ProcessOutcome
{
    DidWork,   // advanced the pipeline; poll again immediately
    Idle,      // nothing to do (no refreshable institution)
    Waiting    // a due time / rate-limit window must elapse before more work is possible
}

public readonly record struct ProcessResult(ProcessOutcome Outcome, TimeSpan Wait, Guid? InstitutionId)
{
    public static ProcessResult DidWork(Guid institutionId) => new(ProcessOutcome.DidWork, TimeSpan.Zero, institutionId);
    public static readonly ProcessResult Idle = new(ProcessOutcome.Idle, TimeSpan.Zero, null);
    public static ProcessResult Waiting(TimeSpan wait, Guid? institutionId = null) =>
        new(ProcessOutcome.Waiting, wait < TimeSpan.Zero ? TimeSpan.Zero : wait, institutionId);
}
