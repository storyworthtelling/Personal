using Codex.Domain.Policy;
using Codex.Infrastructure.Policy;
using Xunit;

namespace Codex.Tests.Policy;

public class PolicyAuditLogCodecTests
{
    [Fact]
    public void RoundTrips_AnAuditLog()
    {
        var log = PolicyAuditLog.Empty
            .Record(new PolicyEvent(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero),
                PolicyEventKind.SuspendFailed, "Could not suspend com.example"))
            .Record(new PolicyEvent(new DateTimeOffset(2026, 8, 21, 9, 5, 0, TimeSpan.Zero),
                PolicyEventKind.AppBlocked, "Blocked com.other"));

        var decoded = PolicyAuditLogCodec.Decode(PolicyAuditLogCodec.Encode(log));

        Assert.Equal(log.Events, decoded.Events);
    }

    [Fact]
    public void EnumsAreWrittenByName_SoTheStoredLogIsReadable()
    {
        var log = PolicyAuditLog.Empty.Record(new PolicyEvent(
            DateTimeOffset.UnixEpoch, PolicyEventKind.CutoverFinalized, "x"));

        Assert.Contains("CutoverFinalized", PolicyAuditLogCodec.Encode(log));
    }

    [Fact]
    public void AbsentOrGarbled_DecodesToEmpty()
    {
        Assert.Empty(PolicyAuditLogCodec.Decode(null).Events);
        Assert.Empty(PolicyAuditLogCodec.Decode("").Events);
        Assert.Empty(PolicyAuditLogCodec.Decode("{ not json ]").Events);
    }
}
