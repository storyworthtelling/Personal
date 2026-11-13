using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

// Efficiency audit §3: every TIDAL search, every pick and every saved track opened the vault and refreshed the
// token, so saving a twelve-track album was twelve vault loads and twelve refreshes. A token is now kept until a
// minute before TIDAL says it expires.
public class TidalTokenCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private static TidalTokenPollResult Token(int expiresIn = 3600) =>
        new(TidalTokenPollStatus.Success, "invented-access", "invented-refresh", expiresIn);

    [Fact]
    public void AFreshTokenIsReused()
    {
        var cache = new TidalTokenCache();
        cache.Keep(Token(), Now);

        Assert.Equal("invented-access", cache.Current(Now.AddMinutes(30))?.AccessToken);
    }

    [Fact]
    public void ATokenIsNotReusedInItsLastMinute()
    {
        var cache = new TidalTokenCache();
        cache.Keep(Token(), Now);

        Assert.Null(cache.Current(Now.AddSeconds(3600 - 59)));
    }

    [Fact]
    public void AFailedRefreshIsNotKept()
    {
        var cache = new TidalTokenCache();
        cache.Keep(new TidalTokenPollResult(TidalTokenPollStatus.Error), Now);

        Assert.Null(cache.Current(Now));
    }

    [Fact]
    public void ATokenWithNoLifetimeIsNotKept()
    {
        var cache = new TidalTokenCache();
        cache.Keep(Token(expiresIn: 0), Now);

        Assert.Null(cache.Current(Now));
    }

    [Fact]
    public void AForgottenTokenIsGone()
    {
        var cache = new TidalTokenCache();
        cache.Keep(Token(), Now);

        cache.Forget();

        Assert.Null(cache.Current(Now));
    }
}
