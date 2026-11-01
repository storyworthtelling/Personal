using Index.Infrastructure.External;
using Storyworthtelling.Diagnostics;
using Xunit;

namespace Index.Tests.Connect;

/// <summary>
/// LOG-LOOP-PLAN.md §4.2 for Plaid Hosted Link: Index hands the link to Chrome and, when it comes back, polls
/// <c>/link/token/get</c> for the session's public_token. Every way that ends is a step on one trail, and the case the
/// plan names — Index came back, polled, and Plaid had no public_token — reads as exactly that, not as a sign-in that
/// never left the app. No token of any kind is ever on the trail.
/// </summary>
public class PlaidLinkTrailTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReturningWithNoPublicTokenAfterPollingIsDiagnosedAsSuch()
    {
        var trail = PlaidLinkTrail.Start(At).Opened(At.AddSeconds(2)).CameBack(At.AddSeconds(40)).NoPublicToken(At.AddSeconds(55), 6);

        Assert.Equal(SignInFlowKinds.PlaidHostedLink, trail.FlowKind);
        Assert.True(trail.IsFinished);
        Assert.Contains("Plaid returned but /link/token/get had no public_token after 6 polls", SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void AConnectedBankIsATokenAndCarriesNoneOfIt()
    {
        var trail = PlaidLinkTrail.Start(At).Opened(At).CameBack(At).Connected(At.AddSeconds(5));

        Assert.True(trail.Succeeded);
        Assert.DoesNotContain("access-", trail.ToLogText());
        Assert.DoesNotContain("public-", trail.ToLogText());
    }

    [Fact]
    public void ALinkTokenThatCouldNotBeMadeIsTheProviderRefusing()
    {
        var trail = PlaidLinkTrail.Start(At).LinkTokenFailed(At, "INVALID_API_KEYS");

        Assert.StartsWith("The provider refused: link_token — INVALID_API_KEYS", SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void ChromeThatWouldNotOpenIsNoBrowser()
    {
        var trail = PlaidLinkTrail.Start(At).ChromeFailed(At, "ActivityNotFoundException");

        Assert.StartsWith("No browser could open the sign-in page", SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void AFailedExchangeNamesTheFailureNotTheToken()
    {
        var trail = PlaidLinkTrail.Start(At).Opened(At).CameBack(At).ExchangeFailed(At, 400, "INVALID_PUBLIC_TOKEN");

        Assert.StartsWith("The redirect came back, but exchanging the code for a token failed: HTTP 400", SignInDiagnosis.Explain(trail));
    }
}
