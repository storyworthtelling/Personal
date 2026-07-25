using Storyboarder.Domain.Model;

namespace Storyboarder.Domain.Pipeline;

// A snapshot of how much of each phase is done for one novel. Decoupled from the EF entities so the
// state machine stays a pure function the worker can drive from query counts.
public sealed record PipelineProgress(
    ProjectStage Stage,
    bool SourceIngested,
    bool AllChunksAnalyzed,
    bool BibleConsolidated,
    bool AllReferenceSheetsRendered,
    bool StoryboardCreated,
    bool AllPanelsRendered,
    bool AllPagesComposed,
    bool AllVolumesAssembled,
    bool AnalysisComplete = false,
    // The novel is a report-only draft: the art stages are skipped entirely and the design bible hands
    // straight over to the revision report. Defaulted false — the illustration is the comprehension
    // probe, so a conversion still wants it unless the owner explicitly asked for the report alone.
    bool ReportOnly = false,
    // Whether the finished outputs have been dealt with by the archive step — either uploaded, skipped
    // because archiving is off, or given up on. Any of those three lets the book finish.
    bool ArchiveComplete = false);

// Decides the next stage for a novel given its progress. Pure and idempotent: a stage only advances
// when its completion predicate holds; Completed and Failed are terminal.
public static class ProjectStateMachine
{
    public static ProjectStage NextStage(PipelineProgress progress) => progress.Stage switch
    {
        ProjectStage.Created => ProjectStage.Ingesting,
        ProjectStage.Ingesting =>
            progress.SourceIngested ? ProjectStage.BuildingDesignBible : ProjectStage.Ingesting,
        // The one branch in the pipeline: a report-only draft leaves the design bible for the revision
        // report directly, skipping storyboarding, panel renders, page composition and volume binding.
        // The bible is still built — the report's cast and continuity come from it.
        ProjectStage.BuildingDesignBible =>
            progress.AllChunksAnalyzed && progress.BibleConsolidated && progress.AllReferenceSheetsRendered
                ? (progress.ReportOnly ? ProjectStage.ComposingAnalysis : ProjectStage.Storyboarding)
                : ProjectStage.BuildingDesignBible,
        ProjectStage.Storyboarding =>
            progress.StoryboardCreated ? ProjectStage.RenderingImages : ProjectStage.Storyboarding,
        ProjectStage.RenderingImages =>
            progress.AllPanelsRendered ? ProjectStage.ComposingPages : ProjectStage.RenderingImages,
        ProjectStage.ComposingPages =>
            progress.AllPagesComposed ? ProjectStage.AssemblingVolumes : ProjectStage.ComposingPages,
        ProjectStage.AssemblingVolumes =>
            progress.AllVolumesAssembled ? ProjectStage.ComposingAnalysis : ProjectStage.AssemblingVolumes,
        // The report is the last thing produced, so archiving comes after it. Archiving is a convenience,
        // not correctness — every output is already on disk — so the Archiving handler advances straight
        // through when archiving is switched off or has given up. A book must never be left unfinished
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
