using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Policy;

public class PolicyAuditLogTests
{
    private static PolicyEvent Event(int minute, PolicyEventKind kind, string summary) =>
        new(new DateTimeOffset(2026, 8, 21, 12, minute, 0, TimeSpan.Zero), kind, summary);

    [Fact]
    public void Record_AppendsEvents()
    {
        var log = PolicyAuditLog.Empty
            .Record(Event(1, PolicyEventKind.AppBlocked, "Blocked com.example"))
            .Record(Event(2, PolicyEventKind.CutoverFinalized, "Debugging locked"));

        Assert.Equal(2, log.Events.Count);
    }

    [Fact]
    public void Record_KeepsOnlyTheNewestMaxEvents()
    {
        var log = PolicyAuditLog.Empty;
        for (var i = 0; i < PolicyAuditLog.MaxEvents + 50; i++)
        {
            log = log.Record(new PolicyEvent(
                new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
                PolicyEventKind.Applied, $"pass {i}"));
        }

        Assert.Equal(PolicyAuditLog.MaxEvents, log.Events.Count);
        // The oldest 50 fell off; the newest is retained.
        Assert.Equal($"pass {PolicyAuditLog.MaxEvents + 49}", log.Events[^1].Summary);
        Assert.DoesNotContain(log.Events, e => e.Summary == "pass 0");
    }

    [Fact]
    public void Format_IsNewestFirst_WithTimestampAndKind()
    {
        var log = PolicyAuditLog.Empty
            .Record(Event(1, PolicyEventKind.AppBlocked, "Blocked A"))
            .Record(Event(5, PolicyEventKind.AppUnblocked, "Unblocked A"));

        var lines = log.Format().Split('\n');
        Assert.Contains("[AppUnblocked]", lines[0]);   // newest first
        Assert.Contains("Unblocked A", lines[0]);
        Assert.Contains("[AppBlocked]", lines[1]);
        Assert.StartsWith("2026-08-21", lines[0]);
    }

    [Fact]
    public void Record_CollapsesConsecutiveDuplicates_SoARecurringFailureIsLoggedOnce()
    {
        var log = PolicyAuditLog.Empty
            .Record(Event(1, PolicyEventKind.SuspendFailed, "Could not suspend com.x"))
            .Record(Event(2, PolicyEventKind.SuspendFailed, "Could not suspend com.x"))  // same pass, dup
            .Record(Event(3, PolicyEventKind.SuspendFailed, "Could not suspend com.x")); // next pass, dup

        Assert.Single(log.Events);

        // A different summary (or the same one after an intervening event) records again.
        log = log.Record(Event(4, PolicyEventKind.AppBlocked, "Blocked com.y"))
                 .Record(Event(5, PolicyEventKind.SuspendFailed, "Could not suspend com.x"));
        Assert.Equal(3, log.Events.Count);
    }

    [Fact]
    public void Format_Empty_IsAFriendlyNote()
    {
        Assert.Equal("No policy events recorded yet.", PolicyAuditLog.Empty.Format());
    }
}
