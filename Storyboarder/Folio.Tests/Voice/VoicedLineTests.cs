using Folio.Domain.Model;
using Folio.Domain.Voice;

namespace Folio.Tests.Voice;

/// <summary>
/// D96 — the Work desk's per-project line, and why it is stored rather than composed when the desk
/// is drawn.
///
/// <para>The bug this reintroduces is composing it on render. The line is written by a model, and
/// the shelf is the screen the app opens on, so a desk that composes as it paints bills a call per
/// book every single time the owner looks at their library — including the paints nobody asked for,
/// like coming back from another screen. Nothing fails; it is simply expensive, silently, in a place
/// no test measures.</para>
/// </summary>
public class VoicedLineTests
{
    [Fact]
    public void ABookWithNoLineYetNeedsOne()
    {
        Assert.True(VoicedLinePlan.NeedsComposing(null, null, ProjectStage.Created));
        Assert.True(VoicedLinePlan.NeedsComposing("   ", ProjectStage.Created, ProjectStage.Created));
    }

    [Fact]
    public void ABookThatHasNotMovedKeepsTheLineItHas()
    {
        Assert.False(VoicedLinePlan.NeedsComposing(
            "Four readers in, and two of them are arguing.",
            ProjectStage.ComposingAnalysis, ProjectStage.ComposingAnalysis));
    }

    [Fact]
    public void ABookThatMovedNeedsANewLine()
    {
        Assert.True(VoicedLinePlan.NeedsComposing(
            "Four readers in, and two of them are arguing.",
            ProjectStage.ComposingAnalysis, ProjectStage.Archiving));
    }

    /// <summary>
    /// A line stored before the app recorded which stage it was for. It cannot be known to still be
    /// true, so it is rewritten once — the alternative is a line that describes the wrong thing for
    /// as long as the book sits still.
    /// </summary>
    [Fact]
    public void ALineWithNoRecordedStageIsRewritten()
    {
        Assert.True(VoicedLinePlan.NeedsComposing("An older line.", null, ProjectStage.Archiving));
    }

    /// <summary>
    /// Every stage is a state the desk can be asked about, so none of them may be one the rule
    /// silently never recomposes for.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryStage))]
    public void EveryStageRecomposesWhenItIsArrivedAt(ProjectStage stage)
    {
        var somewhereElse = stage == ProjectStage.Created ? ProjectStage.Completed : ProjectStage.Created;

        Assert.True(VoicedLinePlan.NeedsComposing("A line about somewhere else.", somewhereElse, stage));
        Assert.False(VoicedLinePlan.NeedsComposing("A line about here.", stage, stage));
    }

    public static TheoryData<ProjectStage> EveryStage()
    {
        var data = new TheoryData<ProjectStage>();
        foreach (var stage in Enum.GetValues<ProjectStage>())
            data.Add(stage);
        return data;
    }
}
