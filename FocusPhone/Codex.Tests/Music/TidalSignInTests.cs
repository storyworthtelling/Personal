using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class PkceCodesTests
{
    // RFC 7636 appendix B's own worked example. If the derivation is right for this,
    // it is right — there is nothing app-specific about it.
    [Fact]
    public void Challenge_MatchesTheWorkedExampleInTheSpec()
    {
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            PkceCodes.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Create_MakesAVerifierTheSpecWouldAccept()
    {
        var codes = PkceCodes.Create();

        // RFC 7636 section 4.1: 43 to 128 characters, unreserved only.
        Assert.InRange(codes.Verifier.Length, 43, 128);
        Assert.All(codes.Verifier, c =>
            Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~', $"'{c}' is not unreserved"));
    }

    [Fact]
    public void Create_DerivesTheChallengeFromTheVerifierItReturns()
    {
        var codes = PkceCodes.Create();

        Assert.Equal(PkceCodes.ChallengeFor(codes.Verifier), codes.Challenge);
    }

    // The verifier is the whole security of the flow: it is the secret the redirect
    // cannot carry. Two sign-ins sharing one would defeat the point of having it.
    [Fact]
    public void Create_IsDifferentEveryTime()
    {
        var verifiers = Enumerable.Range(0, 50).Select(_ => PkceCodes.Create().Verifier).ToHashSet();

        Assert.Equal(50, verifiers.Count);
    }

    [Fact]
    public void Challenge_IsBase64UrlWithNoPadding()
    {
        var challenge = PkceCodes.Create().Challenge;

        Assert.DoesNotContain('=', challenge);
        Assert.DoesNotContain('+', challenge);
        Assert.DoesNotContain('/', challenge);
    }
}

public class TidalSignInTests
{
    private const string Redirect = "com.storyworthtelling.focusphone.audio://tidal-auth";

    // Small local parser rather than a System.Web reference for four assertions.
    private static Dictionary<string, string> Query(string url) =>
        new Uri(url).Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0]),
                          parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);

    [Fact]
    public void AuthorizeUrl_GoesToTidalsLoginHost()
    {
        var url = TidalSignIn.AuthorizeUrl("cid", Redirect, "challenge", "state");

        Assert.StartsWith("https://login.tidal.com/authorize?", url);
    }

    [Fact]
    public void AuthorizeUrl_CarriesEverythingTheFlowNeeds()
    {
        var query = Query(TidalSignIn.AuthorizeUrl("cid", Redirect, "the-challenge", "the-state"));

        Assert.Equal("code", query["response_type"]);
        Assert.Equal("cid", query["client_id"]);
        Assert.Equal(Redirect, query["redirect_uri"]);
        Assert.Equal("the-challenge", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("the-state", query["state"]);
        Assert.False(string.IsNullOrWhiteSpace(query["scope"]));
    }

    [Fact]
    public void AuthorizeUrl_EscapesTheRedirectRatherThanSplittingTheQuery()
    {
        var query = Query(TidalSignIn.AuthorizeUrl("cid", "app://back?x=1&y=2", "c", "s"));

        Assert.Equal("app://back?x=1&y=2", query["redirect_uri"]);
        Assert.Equal("c", query["code_challenge"]);
    }

    [Fact]
    public void NewState_IsUnguessableAndDifferentEveryTime()
    {
        var states = Enumerable.Range(0, 50).Select(_ => TidalSignIn.NewState()).ToHashSet();

        Assert.Equal(50, states.Count);
        Assert.All(states, state => Assert.True(state.Length >= 16));
    }
}

public class OAuthRedirectTests
{
    private const string Redirect = "com.storyworthtelling.focusphone.audio://tidal-auth";

    [Fact]
    public void Read_TakesTheCodeWhenTheStateMatches()
    {
        var result = OAuthRedirect.Read($"{Redirect}?code=abc123&state=s1", "s1");

        Assert.Equal(OAuthRedirectStatus.Authorized, result.Status);
        Assert.Equal("abc123", result.Code);
    }

    // The state is what stops someone else's authorization being fed back into this
    // sign-in. A mismatch is a refusal, never a shrug.
    [Fact]
    public void Read_RefusesACodeWhoseStateIsNotTheOneWeSent()
    {
        var result = OAuthRedirect.Read($"{Redirect}?code=abc123&state=somebody-elses", "s1");

        Assert.Equal(OAuthRedirectStatus.StateMismatch, result.Status);
        Assert.Null(result.Code);
    }

    [Fact]
    public void Read_RefusesACodeWithNoStateAtAll()
    {
        Assert.Equal(
            OAuthRedirectStatus.StateMismatch,
            OAuthRedirect.Read($"{Redirect}?code=abc123", "s1").Status);
    }

    [Fact]
    public void Read_ReportsTheOwnerDecliningSeparatelyFromAFailure()
    {
        var declined = OAuthRedirect.Read($"{Redirect}?error=access_denied&state=s1", "s1");

        Assert.Equal(OAuthRedirectStatus.Declined, declined.Status);
    }

    [Fact]
    public void Read_CarriesAnyOtherErrorThroughForTheLog()
    {
        var result = OAuthRedirect.Read(
            $"{Redirect}?error=invalid_request&error_description=bad+redirect&state=s1", "s1");

        Assert.Equal(OAuthRedirectStatus.Failed, result.Status);
        Assert.Contains("invalid_request", result.Error);
        Assert.Contains("bad redirect", result.Error);
    }

    [Fact]
    public void Read_OnSomethingThatIsNotARedirectAtAll_Fails()
    {
        Assert.Equal(OAuthRedirectStatus.Failed, OAuthRedirect.Read("not a uri", "s1").Status);
        Assert.Equal(OAuthRedirectStatus.Failed, OAuthRedirect.Read(null, "s1").Status);
        Assert.Equal(OAuthRedirectStatus.Failed, OAuthRedirect.Read($"{Redirect}?state=s1", "s1").Status);
    }
}

public class TidalSignInReachabilityTests
{
    // The browser flow only works if the locked phone's browser can reach the pages it
    // sends the owner to. Everything outside this allowlist lands on Chrome's
    // "blocked by your administrator" page, which would make the sign-in a dead end
    // that looks like a bug rather than a policy.
    [Fact]
    public void TheLoginBrowser_CanReachWhereTidalSendsThePerson()
    {
        var allowed = Codex.Domain.Policy.PolicyContext.LoginBrowserUrlAllowlist;

        // The filter matches a host and its subdomains, so the registrable domain
        // covers both login.tidal.com (the password page) and auth.tidal.com (tokens).
        Assert.Contains("tidal.com", allowed);
    }

    [Fact]
    public void TheAuthorizeUrl_IsOnAHostTheBrowserIsAllowedToOpen()
    {
        var host = new Uri(TidalSignIn.AuthorizeEndpoint).Host;
        var allowed = Codex.Domain.Policy.PolicyContext.LoginBrowserUrlAllowlist;

        Assert.Contains(allowed, entry => host == entry || host.EndsWith($".{entry}", StringComparison.Ordinal));
    }
}
