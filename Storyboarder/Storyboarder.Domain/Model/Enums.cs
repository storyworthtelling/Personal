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
    Completed,
    Failed
}

// The status of a single unit of work (a chunk analysis, a reference sheet, a panel render,
// a page composition, a volume assembly). Resumability is built on these.
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
