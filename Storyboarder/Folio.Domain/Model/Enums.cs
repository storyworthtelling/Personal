namespace Folio.Domain.Model;

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

// The kind of story-integrity issue a Finding records. Was positional-only (which of the report's four
// finding lists it sat in); Phase 2 makes it a first-class column so a finding is self-describing and can
// be tracked by identity across drafts. Persisted as int — append new kinds to the end, never reorder.
public enum FindingCategory
{
    PlotHole,
    Inconsistency,
    Inaccuracy,
    Pacing
}

// A finding occurrence's state in the draft it belongs to, relative to its thread's history across the
// project's drafts. New = the thread's first appearance; Carried = it was open in the previous draft and
// still is; Regressed = it was present, then fixed (absent in a later draft), and has now reappeared.
// "Fixed" is NOT a status here — it is derived from a thread that has no occurrence in a given draft.
// Persisted as int — append-only.
public enum FindingStatus
{
    New,
    Carried,
    Regressed
}
