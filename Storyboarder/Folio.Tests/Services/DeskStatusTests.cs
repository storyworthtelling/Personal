using Folio.Infrastructure.Services;
using Xunit;

namespace Folio.Tests.Services;

// What the desk says about a book, at a glance. The three failures this guards were all one screen:
// a heading that said "Reading now" over a book that had stopped, a summary that said "Writing the
// revision report" for a day of work, and an error that was recorded and never shown.
public class DeskStatusTests
{
    private static readonly PhaseProgress None = new(0, 0);

    private static NovelStatus Status(
        string stage = "ComposingAnalysis", string? lastError = null, bool needsAttention = false,
        string? step = "Reading the whole book through — read 3 of 17: Continuity editor",
        int attempts = 0, int maxAttempts = 6, PhaseProgress? notes = null) =>
        new(Guid.NewGuid(), "The Salt Line", stage, lastError, None, None, None,
            notes ?? new PhaseProgress(41, 41), None, AnalysisReady: false, NeedsAttention: needsAttention,
            Step: step, Attempts: attempts, MaxAttempts: maxAttempts);

    [Fact]
    public void ABookBeingReadSaysWhichReadItIsOn()
    {
        var line = DeskStatus.Of(Status());

        Assert.Equal("Reading now", line.Heading);
        Assert.Equal("Reading the whole book through — read 3 of 17: Continuity editor…", line.Summary);
        Assert.Null(line.Problem);
        Assert.False(line.Stuck);
    }

    [Fact]
    public void ABookStillMappingItsChaptersSaysHowFarThroughItIs()
    {
        var line = DeskStatus.Of(Status(step: null, notes: new PhaseProgress(12, 41)));

        Assert.Equal("Analysing the manuscript — chapters 12 of 41…", line.Summary);
    }

    [Fact]
    public void AStoppedBookIsNotUnderTheReadingNowHeading()
    {
        var line = DeskStatus.Of(Status(
            lastError: "Reading pass 'story-integrity': ResponseTruncatedException: truncated (stop_reason=max_tokens)",
            needsAttention: true, attempts: 6));

        Assert.Equal("Needs you", line.Heading);
        Assert.True(line.Stuck);
        Assert.StartsWith("Stopped · ", line.Summary);
        Assert.Contains("read 3 of 17", line.Summary);
        Assert.Equal(
            "The reply was cut off at the output limit. Folio has stopped trying — tap the book, then Retry now.",
            line.Problem);
    }

    [Fact]
    public void ABookThatIsRetryingSaysSoAndHowManyTriesAreUsed()
    {
        var line = DeskStatus.Of(Status(
            lastError: "ConsolidateCraft: IOException: Software caused connection abort",
            attempts: 2));

        Assert.Equal("Reading now", line.Heading);
        Assert.False(line.Stuck);
        Assert.Equal(
            "The connection dropped part-way through the reply — 2 of 6 tries used; trying again.",
            line.Problem);
    }

    [Fact]
    public void AnOfflinePhoneIsWaitingNotFailing()
    {
        var line = DeskStatus.Of(Status(
            lastError: "Reading pass 'x': HttpRequestException: Unable to resolve host \"api.anthropic.com\"",
            attempts: 0));

        Assert.Equal("The phone has no network — waiting to try again.", line.Problem);
    }

    [Fact]
    public void ACompletedBookIsTheLatestAndHasNoProblem()
    {
        var line = DeskStatus.Of(Status(stage: "Completed", step: null));

        Assert.Equal("Latest", line.Heading);
        Assert.Equal("Review delivered.", line.Summary);
        Assert.Null(line.Problem);
    }

    [Fact]
    public void AFailedBookSaysItStoppedAndWhy()
    {
        var line = DeskStatus.Of(Status(stage: "Failed", step: null, lastError: "Ingest: InvalidOperationException: the document was empty"));

        Assert.Equal("Latest", line.Heading);
        Assert.Equal("Stopped.", line.Summary);
        Assert.Equal("The document was empty.", line.Problem);
    }

    // ---- what the owner's voice is told ----------------------------------------------------------

    [Fact]
    public void TheVoiceIsToldAnUnfinishedBookIsUnfinished()
    {
        var facts = DeskStatus.VoiceFacts(Status())!;

        Assert.Contains("NOT finished", facts);
        Assert.Contains("read 3 of 17", facts);
        Assert.Contains("Do not say or imply", facts);
    }

    [Fact]
    public void TheVoiceIsToldACompletedBookIsDone()
    {
        var facts = DeskStatus.VoiceFacts(Status(stage: "Completed", step: null))!;

        Assert.Contains("finished and delivered", facts);
        Assert.DoesNotContain("NOT finished", facts);
    }

    [Fact]
    public void AStoppedBookGetsNoVoicedLineAtAll()
    {
        Assert.Null(DeskStatus.VoiceFacts(Status(lastError: "x", needsAttention: true, attempts: 6)));
    }

    [Fact]
    public void AFailedOrCancelledBookIsToldItDidNotFinish()
    {
        Assert.Contains("did not finish", DeskStatus.VoiceFacts(Status(stage: "Failed", step: null))!);
        Assert.Contains("stopped this review", DeskStatus.VoiceFacts(Status(stage: "Cancelled", step: null))!);
    }
}
