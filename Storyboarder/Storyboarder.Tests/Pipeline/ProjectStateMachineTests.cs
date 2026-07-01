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
        bool storyboardCreated = false,
        bool allPanelsRendered = false,
        bool allPagesComposed = false,
        bool allVolumesAssembled = false,
        bool analysisComplete = false) =>
        new(stage, sourceIngested, allChunksAnalyzed, bibleConsolidated, allReferenceSheetsRendered,
            storyboardCreated, allPanelsRendered, allPagesComposed, allVolumesAssembled, analysisComplete);

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
    public void BuildingDesignBible_Advances_WhenAllComplete() =>
        Assert.Equal(ProjectStage.Storyboarding, ProjectStateMachine.NextStage(
            At(ProjectStage.BuildingDesignBible,
                allChunksAnalyzed: true, bibleConsolidated: true, allReferenceSheetsRendered: true)));

    [Fact]
    public void Storyboarding_Advances_WhenStoryboardCreated() =>
        Assert.Equal(ProjectStage.RenderingImages,
            ProjectStateMachine.NextStage(At(ProjectStage.Storyboarding, storyboardCreated: true)));

    [Fact]
    public void RenderingImages_Advances_WhenAllPanelsRendered() =>
        Assert.Equal(ProjectStage.ComposingPages,
            ProjectStateMachine.NextStage(At(ProjectStage.RenderingImages, allPanelsRendered: true)));

    [Fact]
    public void ComposingPages_Advances_WhenAllPagesComposed() =>
        Assert.Equal(ProjectStage.AssemblingVolumes,
            ProjectStateMachine.NextStage(At(ProjectStage.ComposingPages, allPagesComposed: true)));

    [Fact]
    public void AssemblingVolumes_Advances_ToComposingAnalysis_WhenAllVolumesAssembled() =>
        Assert.Equal(ProjectStage.ComposingAnalysis,
            ProjectStateMachine.NextStage(At(ProjectStage.AssemblingVolumes, allVolumesAssembled: true)));

    [Fact]
    public void ComposingAnalysis_Stays_UntilAnalysisComplete() =>
        Assert.Equal(ProjectStage.ComposingAnalysis,
            ProjectStateMachine.NextStage(At(ProjectStage.ComposingAnalysis)));

    [Fact]
    public void ComposingAnalysis_Advances_ToCompleted_WhenAnalysisComplete() =>
        Assert.Equal(ProjectStage.Completed,
            ProjectStateMachine.NextStage(At(ProjectStage.ComposingAnalysis, analysisComplete: true)));

    [Fact]
    public void Completed_IsTerminal() =>
        Assert.Equal(ProjectStage.Completed, ProjectStateMachine.NextStage(At(ProjectStage.Completed)));

    [Fact]
    public void Failed_IsTerminal() =>
        Assert.Equal(ProjectStage.Failed, ProjectStateMachine.NextStage(At(ProjectStage.Failed)));

    [Fact]
    public void NextStage_IsIdempotent_WhenPredicateUnmet()
    {
        var progress = At(ProjectStage.RenderingImages);
        var once = ProjectStateMachine.NextStage(progress);
        var twice = ProjectStateMachine.NextStage(progress with { Stage = once });
        Assert.Equal(once, twice);
    }
}
