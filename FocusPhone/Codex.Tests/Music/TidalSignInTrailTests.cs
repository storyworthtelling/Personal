using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Storyworthtelling.Diagnostics;
using Xunit;

namespace Codex.Tests.Music;

// The TIDAL sign-in trail (plan §4.2): each way the PKCE flow and the device flow can end becomes a trail step,
// so a sign-in that never comes back (the Error 1002 class: TIDAL's page refusing before any redirect) leaves a
// diagnosis instead of nothing. The flow id is the OAuth state, which Flyleaf's redirect hop logs too.
public class TidalSignInTrailTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static SignInTrail Started() => TidalSignInTrails.StartPkce("state-abc", "client-example", T0).LeftApp(T0.AddSeconds(1));

    [Fact]
    public void StartingRecordsTheThreeThingsThePortalMustAgreeWith()
    {
        var trail = TidalSignInTrails.StartPkce("state-abc", "client-example", T0);

        Assert.Equal("state-abc", trail.FlowId);
        Assert.Equal(SignInFlowKinds.TidalPkce, trail.FlowKind);
        Assert.Contains("client client-example", trail.Steps[0].Detail);
        Assert.Contains(TidalSignIn.RedirectUri, trail.Steps[0].Detail);
        Assert.Contains("user.read", trail.Steps[0].Detail);
    }

    [Fact]
    public void AnAuthorizedRedirectIsReturnedWithTheWholeUrl()
    {
        const string url = "com.storyworthtelling.focusphone.audio://tidal-auth?code=abc&state=state-abc";

        var trail = TidalSignInTrails.Redirect(Started(), url, new OAuthRedirectResult(OAuthRedirectStatus.Authorized, "abc"), T0.AddSeconds(30));

        Assert.Equal(SignInStepKind.Returned, trail.Steps[^1].Kind);
        Assert.Contains("code=abc", trail.Steps[^1].Detail);
        Assert.False(trail.IsFinished);
    }

    [Fact]
    public void ADeclinedRedirectIsTheProvidersAnswer()
    {
        var trail = TidalSignInTrails.Redirect(Started(), "x://y?error=access_denied&state=state-abc",
            new OAuthRedirectResult(OAuthRedirectStatus.Declined, Error: "access_denied"), T0.AddSeconds(30));

        Assert.Equal(SignInStepKind.ProviderError, trail.Steps[^1].Kind);
        Assert.Contains("access_denied", trail.Steps[^1].Detail);
    }

    [Fact]
    public void AMismatchedStateIsRecordedAsOne()
    {
        var trail = TidalSignInTrails.Redirect(Started(), "x://y?code=1&state=state-zzz",
            new OAuthRedirectResult(OAuthRedirectStatus.StateMismatch), T0.AddSeconds(30));

        Assert.Equal(SignInStepKind.StateMismatch, trail.Steps[^1].Kind);
        Assert.Contains("state-zzz", trail.Steps[^1].Detail);
    }

    [Fact]
    public void ARedirectWithNoSignInWaitingIsStale()
    {
        var trail = TidalSignInTrails.Stale("x://y?code=1&state=state-old", T0);

        Assert.Equal("state-old", trail.FlowId);
        Assert.Equal("A redirect arrived from an earlier, abandoned sign-in.", SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void ASuccessfulExchangeIsATokenWithNoTokenInIt()
    {
        var returned = TidalSignInTrails.Redirect(Started(), "x://y?code=abc&state=state-abc", new OAuthRedirectResult(OAuthRedirectStatus.Authorized, "abc"), T0);

        var trail = TidalSignInTrails.Exchange(returned, new TidalTokenPollResult(TidalTokenPollStatus.Success, "ACCESS-SECRET", "REFRESH-SECRET", 3600, "user-1"), T0.AddSeconds(2));

        Assert.True(trail.Succeeded);
        Assert.DoesNotContain("SECRET", trail.ToLogText());
        Assert.Contains("expires", trail.Steps[^1].Detail);
    }

    [Fact]
    public void ARefusedExchangeSaysWhatTidalSaid()
    {
        var trail = TidalSignInTrails.Exchange(Started(), new TidalTokenPollResult(TidalTokenPollStatus.Error, Detail: "invalid_client: Client not found"), T0);

        Assert.Equal(SignInStepKind.ExchangeFailed, trail.Steps[^1].Kind);
        Assert.Contains("invalid_client", trail.Steps[^1].Detail);
    }

    [Fact]
    public void AnExchangeThatThrewIsAnExchangeFailure()
    {
        var trail = TidalSignInTrails.ExchangeThrew(Started(), new HttpRequestException("Connection reset"), T0);

        Assert.Equal(SignInStepKind.ExchangeFailed, trail.Steps[^1].Kind);
        Assert.Contains("HttpRequestException", trail.Steps[^1].Detail);
    }

    [Theory]
    [InlineData(TidalTokenPollStatus.Success, "Signed in")]
    [InlineData(TidalTokenPollStatus.Denied, "The provider refused")]
    [InlineData(TidalTokenPollStatus.Expired, "never left the app")]
    public void TheDeviceFlowEndsTheTrailTheSameWay(TidalTokenPollStatus status, string diagnosis)
    {
        var started = TidalSignInTrails.StartDevice("device-1", "client-example", T0);

        var trail = TidalSignInTrails.DevicePoll(started, new TidalTokenPollResult(status, RefreshToken: status == TidalTokenPollStatus.Success ? "R" : null), T0.AddMinutes(5));

        Assert.Equal(SignInFlowKinds.TidalDevice, trail.FlowKind);
        Assert.True(trail.IsFinished);
        Assert.Contains(diagnosis, SignInDiagnosis.Explain(trail));
    }
}
