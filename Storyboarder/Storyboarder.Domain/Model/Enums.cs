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
    AwaitingApproval, // a panel render was safety-blocked and is parked until the user approves relaxing
                      // the flagged category for that one render (see GraphicNovelProcessor.RenderPanels)

    // A panel render is in flight in a Gemini batch job (opt-in batch mode). Not yet done and not
    // retryable on its own — the worker polls the job and reconciles the result. Appended deliberately:
    // WorkStatus is persisted as its integer value, so new values go at the end.
    SubmittedToBatch
}

public enum ReferenceKind
{
    Character,
    Setting
}

// The camera framing of a panel, which drives its size on the page (weighted layout). Standard is the
// default and lays out exactly like the old uniform grid, so a page with no tagged shots is unchanged;
// the others introduce deliberate variety. Persisted as an integer, so append new values — never insert.
public enum ShotType
{
    Standard,
    // A full-width cinematic band (a wide establishing shot): spans the page across, drawn shorter than
    // a standard tier so it reads as a landscape.
    Wide,
    // A full-page splash for a pivotal beat: the panel gets a page to itself (page splitting isolates it).
    Splash
}

// Whether a panel's dialogue is spoken aloud or thought. Drives how the composer letters it — a spoken
// balloon points a tail into the scene, a thought balloon trails bubbles toward the thinker. Speech is the
// default and matches every panel lettered before this existed. Persisted as an integer, so append new
// values — never insert.
public enum DialogueKind
{
    Speech,
    Thought
}
