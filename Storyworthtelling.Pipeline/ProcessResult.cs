namespace Storyworthtelling.Pipeline;

public enum ProcessOutcome
{
    DidWork,   // advanced the pipeline; poll again immediately
    Idle,      // nothing to do this cycle
    Waiting    // a due time / rate-limit window must elapse before more work is possible
}

public readonly record struct ProcessResult(ProcessOutcome Outcome, TimeSpan Wait, Guid? SubjectId)
{
    public static ProcessResult DidWork(Guid subjectId) => new(ProcessOutcome.DidWork, TimeSpan.Zero, subjectId);
    public static readonly ProcessResult Idle = new(ProcessOutcome.Idle, TimeSpan.Zero, null);
    public static ProcessResult Waiting(TimeSpan wait, Guid? subjectId = null) =>
        new(ProcessOutcome.Waiting, wait < TimeSpan.Zero ? TimeSpan.Zero : wait, subjectId);
}
