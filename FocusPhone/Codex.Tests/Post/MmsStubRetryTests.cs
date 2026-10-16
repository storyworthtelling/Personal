using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

// A download that failed leaves its placeholder row behind, and the carrier keeps the
// message until it expires. These are the ones worth asking for again.
public class MmsStubRetryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 14, 0, 0, TimeSpan.Zero);

    private static MmsStub Stub(
        long id = 1, int minutesOld = 30, string? location = "http://mmsc.example/1",
        string? transaction = "T1", long? expiresInMinutes = null) => new(
        id,
        transaction,
        location,
        Now.AddMinutes(-minutesOld).ToUnixTimeSeconds(),
        expiresInMinutes is { } minutes ? Now.AddMinutes(minutes).ToUnixTimeSeconds() : null);

    [Fact]
    public void AStuckDownload_IsRetried()
    {
        var due = MmsStubRetry.Due([Stub()], new Dictionary<long, int>(), Now);

        Assert.Equal([1L], due.Select(stub => stub.Id));
    }

    // One just announced is probably still downloading; asking again doubles it.
    [Fact]
    public void AFreshDownload_IsLeftToFinish()
    {
        Assert.Empty(MmsStubRetry.Due([Stub(minutesOld: 1)], new Dictionary<long, int>(), Now));
    }

    [Fact]
    public void AnExpiredMessage_IsNotAskedFor()
    {
        Assert.Empty(MmsStubRetry.Due([Stub(expiresInMinutes: -5)], new Dictionary<long, int>(), Now));
    }

    [Fact]
    public void WithNoExpiry_AWeekIsTheLimit()
    {
        Assert.Empty(MmsStubRetry.Due([Stub(minutesOld: 8 * 24 * 60)], new Dictionary<long, int>(), Now));
        Assert.Single(MmsStubRetry.Due([Stub(minutesOld: 6 * 24 * 60)], new Dictionary<long, int>(), Now));
    }

    [Fact]
    public void AfterThreeTries_ItIsLeftAlone()
    {
        var tries = new Dictionary<long, int> { [1] = MmsStubRetry.MaxAttempts };

        Assert.Empty(MmsStubRetry.Due([Stub()], tries, Now));
    }

    [Fact]
    public void WithoutALocation_ThereIsNothingToAskFor()
    {
        Assert.Empty(MmsStubRetry.Due([Stub(location: null), Stub(id: 2, transaction: null)], new Dictionary<long, int>(), Now));
    }
}
