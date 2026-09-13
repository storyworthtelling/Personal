using Folio.Domain.Model;
using Folio.Domain.Pipeline;
using Xunit;

namespace Folio.Tests.Guard;

/// <summary>
/// G3 — the three terminal stages stay terminal.
///
/// <para><c>Completed</c>, <c>Failed</c> and <c>Cancelled</c> each map to themselves in
/// <c>NextStage</c>, and did before P10: this guard reintroduces nothing. It exists because the
/// R-1 investigation found the restart happening <i>anyway</i> — <c>VerifyAndHealOutputsAsync</c>
/// wrote <c>novel.Stage</c> backwards directly, never consulting this function — and the natural
/// next move, once that is fixed, is to conclude the state machine is where finishing is decided
/// and to start editing it. It is one of two places, and this is the other one's guard.</para>
///
/// <para>The predicates are set to their most tempting values: every completion flag true, as a
/// finished book's would be. A terminal stage must not move even then.</para>
/// </summary>
public class TerminalStageTests
{
    public static TheoryData<ProjectStage> TerminalStages =>
        [ProjectStage.Completed, ProjectStage.Failed, ProjectStage.Cancelled];

    [Theory]
    [MemberData(nameof(TerminalStages))]
    public void ATerminalStageNeverAdvances(ProjectStage stage)
    {
        var everythingDone = new PipelineProgress(
            stage,
            SourceIngested: true,
            AllChunksAnalyzed: true,
            BibleConsolidated: true,
            AllReferenceSheetsRendered: true,
            AllSceneImagesRendered: true,
            AnalysisComplete: true,
            ArchiveComplete: true);

        Assert.Equal(stage, ProjectStateMachine.NextStage(everythingDone));
    }

    // And with nothing done, which is what a stage reached by a direct write would look like.
    [Theory]
    [MemberData(nameof(TerminalStages))]
    public void ATerminalStageNeverRetreats(ProjectStage stage)
    {
        var nothingDone = new PipelineProgress(
            stage,
            SourceIngested: false,
            AllChunksAnalyzed: false,
            BibleConsolidated: false,
            AllReferenceSheetsRendered: false);

        Assert.Equal(stage, ProjectStateMachine.NextStage(nothingDone));
    }
}
