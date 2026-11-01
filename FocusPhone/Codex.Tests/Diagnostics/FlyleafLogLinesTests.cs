using Codex.Domain.Calls;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Diagnostics;

// Flyleaf's lines for the log loop (plan §4.5): each policy pass's new events, and each call-surfacing
// decision. Flyleaf kept no file log at all (G3); its decisions reached only logcat, which nobody can read
// after cutover. Fixed wording, because the reader's digest groups on it.
public class FlyleafLogLinesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static PolicyEvent Event(PolicyEventKind kind, string summary, int minute = 0) => new(T0.AddMinutes(minute), kind, summary);

    [Fact]
    public void OnlyEventsTheAuditActuallyRecordsAreNewSoARepeatedFailureIsOneLine()
    {
        var log = PolicyAuditLog.Empty.Record([Event(PolicyEventKind.SuspendFailed, "Could not suspend: com.example.game")]);

        var fresh = log.NewlyRecorded([
            Event(PolicyEventKind.SuspendFailed, "Could not suspend: com.example.game", 5),
            Event(PolicyEventKind.Applied, "12 restrictions · 3 suspended · 40 released · 6 grants · admin off", 5),
        ]);

        var only = Assert.Single(fresh);
        Assert.Equal(PolicyEventKind.Applied, only.Kind);
    }

    [Fact]
    public void ARepeatInsideOneBatchCollapsesToo()
    {
        var fresh = PolicyAuditLog.Empty.NewlyRecorded([
            Event(PolicyEventKind.GrantFailed, "Could not grant X"),
            Event(PolicyEventKind.GrantFailed, "Could not grant X"),
        ]);

        Assert.Single(fresh);
    }

    [Theory]
    [InlineData(PolicyEventKind.Applied, false)]
    [InlineData(PolicyEventKind.AppBlocked, false)]
    [InlineData(PolicyEventKind.AppUnblocked, false)]
    [InlineData(PolicyEventKind.CutoverFinalized, false)]
    [InlineData(PolicyEventKind.SuspendFailed, true)]
    [InlineData(PolicyEventKind.ReleaseFailed, true)]
    [InlineData(PolicyEventKind.GrantFailed, true)]
    [InlineData(PolicyEventKind.ConfigFailed, true)]
    [InlineData(PolicyEventKind.NotDeviceOwner, true)]
    [InlineData(PolicyEventKind.UninstallFailed, true)]
    public void APolicyEventIsAProblemWhenSomethingDidNotTake(PolicyEventKind kind, bool problem)
    {
        Assert.Equal(problem, PolicyLogLines.IsProblem(kind));
    }

    [Fact]
    public void APolicyLineNamesTheKind()
    {
        Assert.Equal("POLICY SuspendFailed: Could not suspend: com.example.game",
            PolicyLogLines.Line(Event(PolicyEventKind.SuspendFailed, "Could not suspend: com.example.game")));
    }

    [Theory]
    [InlineData(CallNoticeKind.Incoming, true, "CALL Incoming: surfaced the call screen")]
    [InlineData(CallNoticeKind.Incoming, false, "CALL Incoming: already surfaced for this ring")]
    [InlineData(CallNoticeKind.Ongoing, true, "CALL Ongoing: surfaced the call screen")]
    public void ACallSurfacingDecisionIsALine(CallNoticeKind kind, bool surfaced, string line)
    {
        Assert.Equal(line, CallNotice.Line(kind, surfaced));
    }

    [Fact]
    public void ACallScreenThatWouldNotOpenSaysSo()
    {
        Assert.Equal("CALL Incoming: the call screen did not open", CallNotice.Line(CallNoticeKind.Incoming, surfaced: true, opened: false));
    }
}
