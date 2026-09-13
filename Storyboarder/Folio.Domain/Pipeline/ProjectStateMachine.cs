using Folio.Domain.Model;

namespace Folio.Domain.Pipeline;

// A snapshot of how much of each phase is done for one manuscript. Decoupled from the EF entities so the
// state machine stays a pure function the worker can drive from query counts.
public sealed record PipelineProgress(
    ProjectStage Stage,
    bool SourceIngested,
    bool AllChunksAnalyzed,
    bool BibleConsolidated,
    bool AllReferenceSheetsRendered,
    // Every scene's evidence image has been rendered or given up on (a refused image is left unavailable,
    // never blocking the report).
    bool AllSceneImagesRendered = false,
    bool AnalysisComplete = false,
    // Whether the finished report has been dealt with by the archive step — either uploaded, skipped
    // because archiving is off, or given up on. Any of those three lets the review finish.
    bool ArchiveComplete = false);

// Decides the next stage for a manuscript given its progress. Pure and idempotent: a stage only advances
// when its completion predicate holds; Completed and Failed are terminal.
public static class ProjectStateMachine
{
    public static ProjectStage NextStage(PipelineProgress progress) => progress.Stage switch
    {
        ProjectStage.Created => ProjectStage.Ingesting,
        ProjectStage.Ingesting =>
            progress.SourceIngested ? ProjectStage.BuildingDesignBible : ProjectStage.Ingesting,
        // The design bible (cast + settings + reference sheets + art style) feeds both the per-scene
        // evidence images and the report's cast/continuity, so it always runs before the images.
        ProjectStage.BuildingDesignBible =>
            progress.AllChunksAnalyzed && progress.BibleConsolidated && progress.AllReferenceSheetsRendered
                ? ProjectStage.RenderingSceneImages
                : ProjectStage.BuildingDesignBible,
        ProjectStage.RenderingSceneImages =>
            progress.AllSceneImagesRendered ? ProjectStage.ComposingAnalysis : ProjectStage.RenderingSceneImages,
        // The report is the last thing produced, so archiving comes after it. Archiving is a convenience,
        // not correctness — the report is already on disk — so the Archiving handler advances straight
        // through when archiving is switched off or has given up. A review must never be left unfinished
        // because a copy could not be uploaded.
        ProjectStage.ComposingAnalysis =>
            progress.AnalysisComplete ? ProjectStage.Archiving : ProjectStage.ComposingAnalysis,
        ProjectStage.Archiving =>
            progress.ArchiveComplete ? ProjectStage.Completed : ProjectStage.Archiving,
        ProjectStage.Completed => ProjectStage.Completed,
        ProjectStage.Failed => ProjectStage.Failed,
        ProjectStage.Cancelled => ProjectStage.Cancelled,
        _ => progress.Stage
    };
}
