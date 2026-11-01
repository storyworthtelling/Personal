using Codex.Domain.Web;
using Storyworthtelling.Diagnostics;
using Xunit;

namespace Codex.Tests.Web;

/// <summary>
/// LOG-LOOP-PLAN.md §4.2: outside Admin mode a suite app's sign-in runs in Flyleaf's restricted browser, so the
/// redirect passes through Flyleaf before it reaches the app. Flyleaf records that hop as its own trail, keyed by
/// the redirect's OAuth <c>state</c> so the reader joins it to the app's trail. A blocked redirect is recorded only
/// when it looks like a sign-in answer; every other blocked page is ordinary browsing.
/// </summary>
public class RedirectHopTrailTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 1, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void AHandedRedirectIsATrailKeyedByItsState()
    {
        var trail = RedirectHop.Handed(new Uri("com.example.app://auth?code=one-time-code&state=f00dfeed"), "com.example.app", At);

        Assert.Equal(SignInFlowKinds.RedirectHop, trail.FlowKind);
        Assert.Equal("f00dfeed", trail.FlowId);
        var hop = Assert.Single(trail.Steps, s => s.Kind == SignInStepKind.Hop);
        Assert.Equal("handed", hop.Outcome);
        Assert.Contains("com.example.app", hop.Detail);
        Assert.Contains(trail.Steps, s => s.Kind == SignInStepKind.Returned && s.Detail.Contains("code=one-time-code"));
    }

    [Fact]
    public void ABlockedSignInAnswerSaysWhy()
    {
        var target = new Uri("https://login.example.com/callback?error=access_denied&state=c0ffee");

        Assert.True(RedirectHop.IsSignInAnswer(target));
        var trail = RedirectHop.Blocked(target, "not on the allowlist", At);

        Assert.Equal("c0ffee", trail.FlowId);
        Assert.True(trail.IsFinished);
        Assert.Contains(trail.Steps, s => s.Kind == SignInStepKind.Hop && s.Outcome == "blocked" && s.Detail.Contains("not on the allowlist"));
    }

    [Fact]
    public void AnOrdinaryBlockedPageIsNotASignIn()
    {
        Assert.False(RedirectHop.IsSignInAnswer(new Uri("https://news.example.com/story?id=4")));
        Assert.True(RedirectHop.IsSignInAnswer(new Uri("com.example.app://auth#access_token=x&state=1")));
    }

    [Fact]
    public void ARedirectWithNoStateStillHasAFlowId()
    {
        var trail = RedirectHop.Handed(new Uri("com.example.app://auth?code=abc"), null, At);

        Assert.Equal(RedirectHop.NoState, trail.FlowId);
        Assert.Contains("(unknown)", trail.Steps.Single(s => s.Kind == SignInStepKind.Hop).Detail);
    }

    [Fact]
    public void AStandingCredentialInTheRedirectIsWithheld()
    {
        var trail = RedirectHop.Handed(new Uri("com.example.app://auth#access_token=sentinel-token-value&state=1"), "com.example.app", At);

        Assert.DoesNotContain("sentinel-token-value", trail.ToLogText());
        Assert.Contains("access_token=[withheld]", trail.ToLogText());
    }
}
