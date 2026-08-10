namespace Storyboarder.Domain.Model;

// The lifecycle of a manuscript as it is turned into a revision report. The background worker
// advances a manuscript through these stages; each stage has a completion predicate. The report is
// the product; the per-scene image is supporting evidence rendered alongside it.
public enum ProjectStage
{
    Created,
    Ingesting,
    BuildingDesignBible,
    RenderingSceneImages,
    ComposingAnalysis,
    Completed,
    Failed,
    Cancelled,   // stopped by the user; terminal, and the worker leaves it alone

    // Uploads the finished report to Google Drive beside the source manuscript, then frees the local
    // copy. Runs between ComposingAnalysis and Completed. Pipeline order lives in ProjectStateMachine,
    // not in this enum's ordering.
    Archiving
}

// The status of a single unit of work (a chunk analysis, a reference sheet, a scene image, the
// report composition). Resumability is built on these.
public enum WorkStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,      // terminal after maxAttempts
    NeedsRetry   // transient (e.g. rate-limited); retried after NextAttemptAtUtc
}

public enum ReferenceKind
{
    Character,
    Setting
}
