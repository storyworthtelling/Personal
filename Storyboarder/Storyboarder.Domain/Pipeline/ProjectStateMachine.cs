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
    bool AnalysisComplete = false);

// Decides the next stage for a novel given its progress. Pure and idempotent: a stage only advances
// when its completion predicate holds; Completed and Failed are terminal.
public static class ProjectStateMachine
{
    public static ProjectStage NextStage(PipelineProgress progress) => progress.Stage switch
    {
        ProjectStage.Created => ProjectStage.Ingesting,
        ProjectStage.Ingesting =>
            progress.SourceIngested ? ProjectStage.BuildingDesignBible : ProjectStage.Ingesting,
        ProjectStage.BuildingDesignBible =>
            progress.AllChunksAnalyzed && progress.BibleConsolidated && progress.AllReferenceSheetsRendered
                ? ProjectStage.Storyboarding
                : ProjectStage.BuildingDesignBible,
        ProjectStage.Storyboarding =>
            progress.StoryboardCreated ? ProjectStage.RenderingImages : ProjectStage.Storyboarding,
        ProjectStage.RenderingImages =>
            progress.AllPanelsRendered ? ProjectStage.ComposingPages : ProjectStage.RenderingImages,
        ProjectStage.ComposingPages =>
            progress.AllPagesComposed ? ProjectStage.AssemblingVolumes : ProjectStage.ComposingPages,
        ProjectStage.AssemblingVolumes =>
            progress.AllVolumesAssembled ? ProjectStage.ComposingAnalysis : ProjectStage.AssemblingVolumes,
        ProjectStage.ComposingAnalysis =>
            progress.AnalysisComplete ? ProjectStage.Completed : ProjectStage.ComposingAnalysis,
        ProjectStage.Completed => ProjectStage.Completed,
        ProjectStage.Failed => ProjectStage.Failed,
        _ => progress.Stage
    };
}
