namespace Storyboarder.Domain.Model;

// The lifecycle of a novel as it is turned into a graphic novel. The background worker
// advances a novel through these stages; each stage has a completion predicate.
public enum ProjectStage
{
    Created,
    Ingesting,
    BuildingDesignBible,
    Storyboarding,
    RenderingImages,
    ComposingPages,
    AssemblingVolumes,
    ComposingAnalysis,
    Completed,
    Failed,
    Cancelled,   // stopped by the user; terminal, and the worker leaves it alone

    // Uploads the finished outputs to Google Drive beside the source manuscript, then frees the local
    // copies. Runs between ComposingAnalysis and Completed. APPENDED DELIBERATELY: the stage is
    // persisted as its integer value, so inserting it in pipeline order would renumber Completed/
    // Failed/Cancelled and silently re-stage every existing novel. Pipeline order lives in
    // ProjectStateMachine, not in this enum's ordering.
    Archiving
}

// The status of a single unit of work (a chunk analysis, a reference sheet, a panel render,
// a page composition, a volume assembly). Resumability is built on these.
public enum WorkStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,           // terminal after maxAttempts
    NeedsRetry,       // transient (e.g. rate-limited); retried after NextAttemptAtUtc
    AwaitingApproval  // a panel render was safety-blocked and is parked until the user approves relaxing
                      // the flagged category for that one render (see GraphicNovelProcessor.RenderPanels)
}

public enum ReferenceKind
{
    Character,
    Setting
}
