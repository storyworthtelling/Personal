namespace Storyworthtelling.Diagnostics.Tests;

// The ordered record of one sign-in (plan §4.2, G2): did a token come back, and if not, where it stopped.
// O9 keeps the full redirect URL; A3 keeps every standing credential out, by construction.
public class SignInTrailTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static SignInTrail Gmail() => SignInTrail.Start(
        SignInFlowKinds.GmailApi, "flow-1", T0,
        clientId: null, redirectUri: null,
        scopes: ["https://www.googleapis.com/auth/gmail.modify"],
        browserPath: SignInBrowserPaths.AccountPicker);

    [Fact]
    public void StartingRecordsKindScopesAndPath()
    {
        var trail = Gmail();

        var started = Assert.Single(trail.Steps);
        Assert.Equal(SignInStepKind.Started, started.Kind);
        Assert.Equal(SignInFlowKinds.GmailApi, trail.FlowKind);
        Assert.Equal("flow-1", trail.FlowId);
        Assert.Contains("gmail.modify", started.Detail);
        Assert.Contains(SignInBrowserPaths.AccountPicker, started.Detail);
        Assert.False(trail.IsFinished);
    }

    [Fact]
    public void StepsStayInTheOrderTheyHappened()
    {
        var trail = Gmail()
            .AccountChosen(T0.AddSeconds(4))
            .ConsentShown(T0.AddSeconds(5))
            .TokenReceived(T0.AddSeconds(20), ["gmail.modify", "drive"], T0.AddMinutes(45), "Bearer");

        Assert.Equal(
            [SignInStepKind.Started, SignInStepKind.AccountPicker, SignInStepKind.ConsentShown, SignInStepKind.TokenReceived],
            trail.Steps.Select(s => s.Kind));
        Assert.True(trail.IsFinished);
        Assert.True(trail.Succeeded);
    }

    [Fact]
    public void TheTokenStepTakesScopesExpiryAndTypeAndHasNowhereToPutATheToken()
    {
        var trail = Gmail().TokenReceived(T0.AddSeconds(3), ["gmail.modify"], T0.AddMinutes(45), "Bearer");

        var step = trail.Steps[^1];
        Assert.Contains("gmail.modify", step.Detail);
        Assert.Contains("Bearer", step.Detail);

        // A3 by construction: TokenReceived has no parameter that could carry a token value.
        var parameters = typeof(SignInTrail).GetMethod(nameof(SignInTrail.TokenReceived))!.GetParameters();
        Assert.DoesNotContain(parameters, p => p.Name!.Contains("token", StringComparison.OrdinalIgnoreCase)
            && !p.Name.Equals("tokenType", StringComparison.Ordinal));
    }

    [Fact]
    public void TheFullRedirectUrlIsKeptIncludingTheOneTimeCode()
    {
        var trail = Gmail().Returned(T0.AddSeconds(30),
            "com.example.app://auth?code=abc123def&state=flow-1");

        Assert.Contains("code=abc123def", trail.Steps[^1].Detail);
        Assert.Contains("state=flow-1", trail.Steps[^1].Detail);
    }

    [Fact]
    public void StandingCredentialsInARedirectAreWithheld()
    {
        var trail = Gmail().Returned(T0.AddSeconds(30),
            "com.example.app://auth#access_token=SECRETVALUE1&refresh_token=SECRETVALUE2&code_verifier=SECRETVALUE3&client_secret=SECRETVALUE4&password=SECRETVALUE5&code=keep");

        var detail = trail.Steps[^1].Detail;
        Assert.DoesNotContain("SECRETVALUE", detail);
        Assert.Contains("access_token=[withheld]", detail);
        Assert.Contains("code=keep", detail);
    }

    [Fact]
    public void ResumingWithoutAReturnAfterLeavingIsNoReturn()
    {
        var trail = Gmail().LeftApp(T0.AddSeconds(1));

        var checkedTrail = trail.CheckNoReturn(T0.AddSeconds(41), resumed: true);

        Assert.Equal(SignInStepKind.NoReturn, checkedTrail.Steps[^1].Kind);
        Assert.Contains("resumed", checkedTrail.Steps[^1].Detail);
        Assert.True(checkedTrail.IsFinished);
    }

    [Fact]
    public void AResumeAfterSomethingCameBackIsNotNoReturn()
    {
        // The account picker answered before the app resumed; the consent step is still under way.
        var trail = Gmail().LeftApp(T0.AddSeconds(1)).AccountChosen(T0.AddSeconds(5));

        Assert.False(trail.CheckNoReturn(T0.AddSeconds(6), resumed: true).IsFinished);
    }

    [Fact]
    public void AResumeWithOnlyFlyleafsHopSinceLeavingIsStillNoReturn()
    {
        var trail = Gmail().LeftApp(T0.AddSeconds(1)).Hop(T0.AddSeconds(10), "com.example.app", null);

        Assert.True(trail.CheckNoReturn(T0.AddSeconds(30), resumed: true).IsFinished);
    }

    [Fact]
    public void TenMinutesWithNothingIsNoReturnAndLessIsStillWaiting()
    {
        var trail = Gmail().LeftApp(T0.AddSeconds(1));

        Assert.False(trail.CheckNoReturn(T0.AddMinutes(9), resumed: false).IsFinished);
        var timedOut = trail.CheckNoReturn(T0.AddMinutes(10), resumed: false);

        Assert.Equal(SignInStepKind.NoReturn, timedOut.Steps[^1].Kind);
        Assert.Contains("timed out", timedOut.Steps[^1].Detail);
        Assert.Equal(TimeSpan.FromMinutes(10), SignInTrail.NoReturnTimeout);
    }

    [Fact]
    public void AFinishedTrailIsNotMarkedNoReturnLater()
    {
        var trail = Gmail().AccountCancelled(T0.AddSeconds(3));

        var after = trail.CheckNoReturn(T0.AddHours(1), resumed: true);

        Assert.Same(trail, after);
    }

    [Fact]
    public void ProviderErrorsStateProblemsAndExchangeFailuresAreRecorded()
    {
        var provider = Gmail().ProviderError(T0, "access_denied", "The user denied access");
        var mismatch = Gmail().StateMismatch(T0, expected: "flow-1", received: "flow-0");
        var stale = Gmail().StaleRedirect(T0, received: "flow-0");
        var exchange = Gmail().ExchangeFailed(T0, 400, "{\"error\":\"invalid_grant\"}");

        Assert.Contains("access_denied", provider.Steps[^1].Detail);
        Assert.Contains("flow-0", mismatch.Steps[^1].Detail);
        Assert.Equal(SignInStepKind.StaleRedirect, stale.Steps[^1].Kind);
        Assert.Contains("400", exchange.Steps[^1].Detail);
        Assert.Contains("invalid_grant", exchange.Steps[^1].Detail);
        Assert.All(new[] { provider, mismatch, stale, exchange }, t => Assert.True(t.IsFinished));
    }

    [Fact]
    public void TheLogTextNamesTheFlowAndEveryStepAndTheDiagnosis()
    {
        var trail = Gmail().AccountCancelled(T0.AddSeconds(3));

        var text = trail.ToLogText();

        Assert.StartsWith("SIGN-IN gmail-api flow-1", text);
        Assert.Contains("Started", text);
        Assert.Contains("AccountPicker", text);
        Assert.Contains("Why: Google account picker cancelled.", text);
    }

    [Fact]
    public void ATrailSurvivesBeingWrittenDownAndReadBack()
    {
        var trail = Gmail().LeftApp(T0.AddSeconds(2)).Returned(T0.AddSeconds(9), "x://y?code=1&state=flow-1");

        var restored = SignInTrail.FromJson(trail.ToJson());

        Assert.Equal(trail.ToLogText(), restored.ToLogText());
    }

    [Fact]
    public void ANewFlowIdIsRandom()
    {
        Assert.NotEqual(SignInTrail.NewFlowId(), SignInTrail.NewFlowId());
    }
}
