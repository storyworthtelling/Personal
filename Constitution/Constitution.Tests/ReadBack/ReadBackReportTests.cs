using System.Net;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.ReadBack;

namespace Constitution.Tests.ReadBack;

/// <summary>The one word Diagnostics keeps for a morning's read-back.</summary>
public class ReadBackReportTests
{
    private static DocumentReadBack Settled => new DocumentReadBack.Settled("a", 2, 0);

    private static DocumentReadBack Failed(Exception failure, ReadBackStage stage = ReadBackStage.Download) =>
        new DocumentReadBack.Failed("b", stage, failure);

    private static DiagnosticsOutcome Outcome(params DocumentReadBack[] documents) => new ReadBackReport(documents).Outcome;

    [Fact]
    public void AMorningWithNothingToDoSucceeded() => Assert.Equal(DiagnosticsOutcome.Succeeded, Outcome());

    [Fact]
    public void SettledAndDigestsAndAbsentPagesAreAllDone() =>
        Assert.Equal(DiagnosticsOutcome.Succeeded, Outcome(
            Settled, new DocumentReadBack.DigestTrashed("d"), new DocumentReadBack.DigestKept("e"),
            new DocumentReadBack.NotOnTablet("f"), new DocumentReadBack.GaveUp("g")));

    [Fact]
    public void NoMarksInTheCloudIsFoundNothingRead()
    {
        Assert.Equal(DiagnosticsOutcome.FoundNothingRead, Outcome(new DocumentReadBack.NoMarksInCloud("a")));
        Assert.Equal(DiagnosticsOutcome.FoundNothingRead, Outcome(Settled, new DocumentReadBack.NoMarksInCloud("a"), Failed(new IOException("x"))));
    }

    [Fact]
    public void ARefusedTokenIsRefused()
    {
        var refused = new HttpRequestException("no", null, HttpStatusCode.Unauthorized);

        Assert.Equal(DiagnosticsOutcome.Refused, Outcome(Failed(refused)));
        Assert.Equal(DiagnosticsOutcome.Refused, Outcome(Failed(new HttpRequestException("no", null, HttpStatusCode.Forbidden), ReadBackStage.Read)));
    }

    [Fact]
    public void ADroppedConnectionIsUnreachable()
    {
        Assert.Equal(DiagnosticsOutcome.Unreachable, Outcome(Failed(new HttpRequestException("down"))));
        Assert.Equal(DiagnosticsOutcome.Unreachable, Outcome(Failed(new TaskCanceledException("late", new TimeoutException()))));
    }

    [Fact]
    public void SomeDoneAndSomeNotIsPartial()
    {
        Assert.Equal(DiagnosticsOutcome.Partial, Outcome(Settled, Failed(new HttpRequestException("down"))));
        Assert.Equal(DiagnosticsOutcome.Partial, Outcome(Settled, new DocumentReadBack.Unfinished("c", "a box was not read")));
    }

    [Fact]
    public void ALocalFailureAloneIsPartialNotUnreachable()
    {
        Assert.Equal(DiagnosticsOutcome.Partial, Outcome(Failed(new IOException("disk"), ReadBackStage.Persist)));
        Assert.Equal(DiagnosticsOutcome.Partial, Outcome(new DocumentReadBack.Unfinished("c", "a box was not read")));
    }
}
