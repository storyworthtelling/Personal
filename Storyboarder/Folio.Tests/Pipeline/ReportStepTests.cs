using Folio.Domain.Pipeline;
using Xunit;

namespace Folio.Tests.Pipeline;

// "Writing the revision report" was one sentence for everything from the first reduction to the last
// page, which is a day or more of work. The step says which part of it the book is actually in.
public class ReportStepTests
{
    private static readonly ReportProgress Everything = new(
        StoryReport: true, Craft: true, Voice: true, Findings: true, Expansion: true, SeriesPending: false,
        ReadsDone: 17, ReadsTotal: 17, NextRead: null, JacketPending: false, Rendered: false);

    [Fact]
    public void WhileReadsRemainItNamesTheReadAndWhichOneItIs()
    {
        var step = ReportStep.Describe(Everything with { ReadsDone = 2, NextRead = "Continuity editor" });

        Assert.Equal("Reading the whole book through — read 3 of 17: Continuity editor", step);
    }

    /// <summary>After the reads, the round table says which round it is on, out of about how many (release H).</summary>
    [Fact]
    public void AfterTheReadsTheRoundTableSaysWhichRoundItIsOn()
    {
        Assert.Equal("Round table — round 12 of about 30",
            ReportStep.Describe(Everything with { RoundTableRounds = 12, RoundTableCap = 30 }));
        Assert.Equal("Round table — round 1 of about 30",
            ReportStep.Describe(Everything with { RoundTableRounds = 0, RoundTableCap = 30 }));
    }

    [Fact]
    public void AClosedRoundTableHandsOnToTheJacketAndThePages()
    {
        var step = ReportStep.Describe(Everything with { RoundTableRounds = 31, RoundTableCap = 30, RoundTableClosed = true });

        Assert.Equal("Composing the report", step);
    }

    [Fact]
    public void TheFirstRead_IsReadOne()
    {
        var step = ReportStep.Describe(Everything with { ReadsDone = 0, NextRead = "Story integrity" });

        Assert.Equal("Reading the whole book through — read 1 of 17: Story integrity", step);
    }

    [Theory]
    [InlineData("StoryReport", "Drawing the chapter notes into the story report")]
    [InlineData("Craft", "Gathering the line edits, prose and dialogue notes")]
    [InlineData("Voice", "Checking the voice for drift")]
    [InlineData("Findings", "Matching findings against the last draft")]
    [InlineData("Expansion", "Looking for scenes to add")]
    public void EachReductionIsNamedWhileItIsTheOneBeingDone(string missing, string expected)
    {
        var progress = missing switch
        {
            "StoryReport" => Everything with { StoryReport = false },
            "Craft" => Everything with { Craft = false },
            "Voice" => Everything with { Voice = false },
            "Findings" => Everything with { Findings = false },
            _ => Everything with { Expansion = false },
        };

        Assert.Equal(expected, ReportStep.Describe(progress));
    }

    [Fact]
    public void TheReductionsComeBeforeTheReadsInTheOrderTheyRun()
    {
        // Craft is missing AND reads remain: the reductions run first, so craft is the current step.
        var step = ReportStep.Describe(Everything with { Craft = false, ReadsDone = 0, NextRead = "Story integrity" });

        Assert.Equal("Gathering the line edits, prose and dialogue notes", step);
    }

    [Fact]
    public void ASequelChecksContinuityAgainstThePreviousBookBeforeTheReads()
    {
        var step = ReportStep.Describe(Everything with { SeriesPending = true, ReadsDone = 0, NextRead = "Story integrity" });

        Assert.Equal("Checking continuity against the previous book", step);
    }

    [Fact]
    public void AfterTheReadsComeTheJacketAndThenThePages()
    {
        Assert.Equal("Painting the jacket", ReportStep.Describe(Everything with { JacketPending = true }));
        Assert.Equal("Composing the report", ReportStep.Describe(Everything));
    }

    /// <summary>Each scene is judged in its own call before the reads begin, and the desk says which.</summary>
    [Fact]
    public void WhileScenesRemainItSaysWhichSceneIsBeingJudged()
    {
        var step = ReportStep.Describe(Everything with { ScenesJudged = 3, ScenesTotal = 40, ReadsDone = 0 });

        Assert.Equal("Judging the scenes — scene 4 of 40", step);
    }
}
