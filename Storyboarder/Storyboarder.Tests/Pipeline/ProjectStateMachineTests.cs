using Storyboarder.Domain.Model;
using Storyboarder.Domain.Pipeline;
using Xunit;

namespace Storyboarder.Tests.Pipeline;

public class ProjectStateMachineTests
{
    private static PipelineProgress At(ProjectStage stage,
        bool sourceIngested = false,
        bool allChunksAnalyzed = false,
        bool bibleConsolidated = false,
        bool allReferenceSheetsRendered = false,
        bool allSceneImagesRendered = false,
        bool analysisComplete = false,
        bool archiveComplete = false) =>
        new(stage, sourceIngested, allChunksAnalyzed, bibleConsolidated, allReferenceSheetsRendered,
            allSceneImagesRendered, analysisComplete, archiveComplete);

    [Fact]
    public void Created_Advances_ToIngesting() =>
        Assert.Equal(ProjectStage.Ingesting, ProjectStateMachine.NextStage(At(ProjectStage.Created)));

    [Fact]
    public void Ingesting_Stays_UntilSourceIngested() =>
        Assert.Equal(ProjectStage.Ingesting, ProjectStateMachine.NextStage(At(ProjectStage.Ingesting)));

    [Fact]
    public void Ingesting_Advances_WhenSourceIngested() =>
        Assert.Equal(ProjectStage.BuildingDesignBible,
            ProjectStateMachine.NextStage(At(ProjectStage.Ingesting, sourceIngested: true)));

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void BuildingDesignBible_Stays_WhenAnyPartIncomplete(bool chunks, bool bible, bool sheets) =>
        Assert.Equal(ProjectStage.BuildingDesignBible, ProjectStateMachine.NextStage(
            At(ProjectStage.BuildingDesignBible,
                allChunksAnalyzed: chunks, bibleConsolidated: bible, allReferenceSheetsRendered: sheets)));

    [Fact]
    public void BuildingDesignBible_Advances_ToRenderingSceneImages_WhenAllComplete() =>
        Assert.Equal(ProjectStage.RenderingSceneImages, ProjectStateMachine.NextStage(
            At(ProjectStage.BuildingDesignBible,
                allChunksAnalyzed: true, bibleConsolidated: true, allReferenceSheetsRendered: true)));

    [Fact]
    public void RenderingSceneImages_Stays_UntilAllImagesRendered() =>
        Assert.Equal(ProjectStage.RenderingSceneImages,
            ProjectStateMachine.NextStage(At(ProjectStage.RenderingSceneImages)));

    [Fact]
    public void RenderingSceneImages_Advances_ToComposingAnalysis_WhenAllImagesRendered() =>
        Assert.Equal(ProjectStage.ComposingAnalysis,
            ProjectStateMachine.NextStage(At(ProjectStage.RenderingSceneImages, allSceneImagesRendered: true)));

    [Fact]
    public void ComposingAnalysis_Stays_UntilAnalysisComplete() =>
        Assert.Equal(ProjectStage.ComposingAnalysis,
            ProjectStateMachine.NextStage(At(ProjectStage.ComposingAnalysis)));

    // The report is the last thing produced, so archiving follows it rather than preceding it.
    [Fact]
    public void ComposingAnalysis_Advances_ToArchiving_WhenAnalysisComplete() =>
        Assert.Equal(ProjectStage.Archiving,
            ProjectStateMachine.NextStage(At(ProjectStage.ComposingAnalysis, analysisComplete: true)));

    [Fact]
    public void Archiving_Waits_UntilTheArchiveStepIsDone() =>
        Assert.Equal(ProjectStage.Archiving,
            ProjectStateMachine.NextStage(At(ProjectStage.Archiving)));

    // "Done" covers uploaded, skipped because archiving is off, and given up on — a review must never be
    // left unfinished because a copy could not be uploaded.
    [Fact]
    public void Archiving_Advances_ToCompleted_WhenTheArchiveStepIsDone() =>
        Assert.Equal(ProjectStage.Completed,
            ProjectStateMachine.NextStage(At(ProjectStage.Archiving, archiveComplete: true)));

    [Fact]
    public void Completed_IsTerminal() =>
        Assert.Equal(ProjectStage.Completed, ProjectStateMachine.NextStage(At(ProjectStage.Completed)));

    [Fact]
    public void Failed_IsTerminal() =>
        Assert.Equal(ProjectStage.Failed, ProjectStateMachine.NextStage(At(ProjectStage.Failed)));

    [Fact]
    public void Cancelled_IsTerminal() =>
        Assert.Equal(ProjectStage.Cancelled, ProjectStateMachine.NextStage(At(ProjectStage.Cancelled)));

    [Fact]
    public void NextStage_IsIdempotent_WhenPredicateUnmet()
    {
        var progress = At(ProjectStage.RenderingSceneImages);
        var once = ProjectStateMachine.NextStage(progress);
        var twice = ProjectStateMachine.NextStage(progress with { Stage = once });
        Assert.Equal(once, twice);
    }
}
