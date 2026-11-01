namespace Storyworthtelling.Diagnostics.Tests;

// SignInDiagnosis.Explain turns a trail into the one reason sentence written beside it (plan §4.2). One case
// per sentence, so a new way for a sign-in to stop is a deliberate edit here.
public class SignInDiagnosisTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static SignInTrail Browser(string kind = SignInFlowKinds.TidalPkce) => SignInTrail.Start(
        kind, "state-1", T0, "client-example", "com.example.app://auth", ["user.read"], SignInBrowserPaths.RestrictedBrowser);

    private static SignInTrail Device() => SignInTrail.Start(
        SignInFlowKinds.GmailApi, "flow-1", T0, null, null, ["gmail.modify"], SignInBrowserPaths.AccountPicker);

    [Fact]
    public void Success()
    {
        var trail = Device().TokenReceived(T0.AddSeconds(5), ["gmail.modify"], T0.AddMinutes(45), "Bearer");

        Assert.Equal("Signed in: a token came back with gmail.modify.", SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void StillInProgress()
    {
        Assert.Equal("The sign-in is still in progress.", SignInDiagnosis.Explain(Browser().LeftApp(T0)));
    }

    [Fact]
    public void NoBrowser()
    {
        var trail = Browser().NoBrowser(T0, "ActivityNotFoundException: no activity for VIEW https");

        Assert.Equal(
            "No browser could open the sign-in page: ActivityNotFoundException: no activity for VIEW https.",
            SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void BrowserRefusedByPolicy()
    {
        var trail = Browser().BrowserRefused(T0, "Chrome suspended outside Admin mode");

        Assert.Equal(
            "The sign-in page was never opened: Chrome suspended outside Admin mode.",
            SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void NeverRedirectedAndCameBackByBack()
    {
        var trail = Browser().LeftApp(T0).CheckNoReturn(T0.AddSeconds(40), resumed: true);

        Assert.Equal(
            "Opened the tidal-pkce page; no redirect ever reached Flyleaf or the app; came back by Back after 40 s. "
            + "The provider page most likely refused the request before redirecting (scope or redirect URI not registered).",
            SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void NeverRedirectedAndTimedOut()
    {
        var trail = Browser().LeftApp(T0).CheckNoReturn(T0.AddMinutes(10), resumed: false);

        Assert.Equal(
            "Opened the tidal-pkce page; no redirect ever reached Flyleaf or the app; nothing came back within 10 minutes. "
            + "The provider page most likely refused the request before redirecting (scope or redirect URI not registered).",
            SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void HandedBackByFlyleafButNeverArrived()
    {
        var trail = Browser().LeftApp(T0)
            .Hop(T0.AddSeconds(20), handedTo: "com.example.score", blockedReason: null)
            .CheckNoReturn(T0.AddSeconds(60), resumed: true);

        Assert.Equal(
            "Flyleaf handed the redirect to com.example.score, but it never reached the app.",
            SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void BlockedAtFlyleafsHop()
    {
        var trail = Browser().LeftApp(T0).Hop(T0.AddSeconds(20), handedTo: null, blockedReason: "scheme not in SuiteRedirect's list");

        Assert.Equal(
            "Redirect reached Flyleaf's browser and was blocked: scheme not in SuiteRedirect's list.",
            SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void ProviderError()
    {
        var trail = Browser().ProviderError(T0, "invalid_scope", "Scope r_usr is not allowed");

        Assert.Equal("The provider refused: invalid_scope — Scope r_usr is not allowed.", SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void StateMismatch()
    {
        var trail = Browser().StateMismatch(T0, expected: "state-1", received: "state-9");

        Assert.Equal(
            "A redirect came back for a different sign-in (state did not match); this one was not completed.",
            SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void StaleRedirect()
    {
        var trail = Browser().StaleRedirect(T0, received: "state-0");

        Assert.Equal("A redirect arrived from an earlier, abandoned sign-in.", SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void ExchangeFailed()
    {
        var trail = Browser().Returned(T0, "com.example.app://auth?code=1&state=state-1").ExchangeFailed(T0, 401, "invalid_client");

        Assert.Equal(
            "The redirect came back, but exchanging the code for a token failed: HTTP 401.",
            SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void ReturnedButNoTokenFollowed()
    {
        var trail = Browser().Returned(T0, "com.example.app://auth?code=1&state=state-1")
            .CheckNoReturn(T0.AddMinutes(10), resumed: false);

        Assert.Equal("The redirect came back, but no token followed.", SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void PickerCancelled()
    {
        Assert.Equal("Google account picker cancelled.", SignInDiagnosis.Explain(Device().AccountCancelled(T0)));
    }

    [Fact]
    public void ConsentDenied()
    {
        var trail = Device().AccountChosen(T0).ConsentShown(T0).ConsentDenied(T0.AddSeconds(8), "OperationCanceledException");

        Assert.Equal("The consent screen was declined (OperationCanceledException).", SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void RecoverableAuthNeverCompleted()
    {
        var trail = Device().AccountChosen(T0).RecoverableAuth(T0, "NeedPermission")
            .CheckNoReturn(T0.AddMinutes(10), resumed: false);

        Assert.Equal(
            "Google asked for the user to act (NeedPermission), and it was not completed.",
            SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void DeviceConsentNeverAnswered()
    {
        var trail = Device().AccountChosen(T0).ConsentShown(T0).CheckNoReturn(T0.AddMinutes(10), resumed: false);

        Assert.Equal(
            "The Google account was chosen, but the consent step never answered.",
            SignInDiagnosis.Explain(trail));
    }

    // A flow with its own clock (Plaid's polling, a device code's window) says what ran out, not a generic no-return.
    [Fact]
    public void AnOwnClockThatRanOutAfterComingBackSaysWhat()
    {
        var at = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        var trail = SignInTrail.Start(SignInFlowKinds.PlaidHostedLink, "f1", at, null, null, [], SignInBrowserPaths.ChromeByPackage)
            .LeftApp(at).Returned(at.AddSeconds(30), "(came back)").TimedOut(at.AddSeconds(45), "no public_token after 6 polls");

        Assert.Equal("The redirect came back, but no token followed: no public_token after 6 polls.", SignInDiagnosis.Explain(trail));
    }

    [Fact]
    public void AnOwnClockThatRanOutWithoutComingBackSaysWhat()
    {
        var at = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        var trail = SignInTrail.Start(SignInFlowKinds.TidalDevice, "f2", at, null, null, [], SignInBrowserPaths.None)
            .TimedOut(at.AddMinutes(5), "the device code's window closed before it was approved");

        Assert.Equal("No token came back: the device code's window closed before it was approved.", SignInDiagnosis.Explain(trail));
    }
}
