namespace Storyworthtelling.Google.Tests;

/// <summary>
/// The device-account token cache every app kept a copy of (audit 01 §10): one token reused until shortly before
/// it expires, and the stale one handed back so the platform can be told to forget it before a fresh one is asked
/// for. The Android half only supplies the fetch.
/// </summary>
public class CachedAccessTokenTests
{
    private DateTimeOffset now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private CachedAccessToken Make() => new(TimeSpan.FromMinutes(45), () => now);

    [Fact]
    public async Task ATokenIsReusedUntilItsTimeIsUp()
    {
        var cache = Make();
        var fetches = 0;

        var first = await cache.GetAsync(_ => $"token-{++fetches}");
        now = now.AddMinutes(44);
        var second = await cache.GetAsync(_ => $"token-{++fetches}");

        Assert.Equal("token-1", first);
        Assert.Equal("token-1", second);
        Assert.Equal(1, fetches);
    }

    [Fact]
    public async Task AnExpiredTokenIsHandedBackToBeForgottenBeforeAFreshOne()
    {
        var cache = Make();
        await cache.GetAsync(_ => "old");
        now = now.AddMinutes(46);

        string? stale = null;
        var fresh = await cache.GetAsync(previous => { stale = previous; return "new"; });

        Assert.Equal("old", stale);
        Assert.Equal("new", fresh);
    }

    [Fact]
    public async Task TheFirstFetchHasNothingToForget()
    {
        string? stale = "unset";
        await Make().GetAsync(previous => { stale = previous; return "t"; });

        Assert.Null(stale);
    }

    [Fact]
    public async Task AFailedFetchCachesNothing()
    {
        var cache = Make();
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync(_ => throw new InvalidOperationException("no account")));

        Assert.Equal("t", await cache.GetAsync(_ => "t"));
    }

    [Fact]
    public void ATokenTypeIsOauth2AndTheScopesInOrder() =>
        Assert.Equal("oauth2:" + GoogleScopes.Calendar + " " + GoogleScopes.Drive, GoogleScopes.ToTokenType(GoogleScopes.Drive, GoogleScopes.Calendar));
}

/// <summary>
/// A token Google rejected is forgotten at once (audit 11 §4): it used to be handed out for the rest of its 45 minutes,
/// so every Drive call in that time failed the same way. A different token — one fetched since — is left alone.
/// </summary>
public class RejectedTokenTests
{
    private DateTimeOffset now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ARejectedTokenIsForgottenAndTheNextCallFetchesAFreshOneHandingTheOldOneBack()
    {
        var cache = new CachedAccessToken(TimeSpan.FromMinutes(45), () => now);
        await cache.GetAsync(_ => "rejected");

        Assert.True(cache.Forget("rejected"));
        Assert.False(cache.Forget("rejected"));
        string? stale = null;
        var fresh = await cache.GetAsync(previous => { stale = previous; return "fresh"; });

        Assert.Equal("fresh", fresh);
        Assert.Equal("rejected", stale);
    }

    [Fact]
    public async Task ForgettingAnOlderTokenLeavesTheCurrentOneInPlace()
    {
        var cache = new CachedAccessToken(TimeSpan.FromMinutes(45), () => now);
        await cache.GetAsync(_ => "current");

        Assert.False(cache.Forget("older"));
        var again = await cache.GetAsync(_ => "fetched");

        Assert.Equal("current", again);
    }

    [Fact]
    public void ASignInGoogleTurnedAwayIsItsOwnKindOfFailureThatOldCatchesStillHold()
    {
        var refused = new GoogleSignInRequiredException("Google turned the sign-in away.");

        Assert.IsAssignableFrom<InvalidOperationException>(refused);
        Assert.Null(refused.Recovery);
    }
}
