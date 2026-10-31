using System.Globalization;

namespace Storyworthtelling.Diagnostics;

/// <summary>
/// Turns a <see cref="SignInTrail"/> into the one reason sentence written beside it in the log
/// (LOG-LOOP-PLAN.md §4.2). The reader's digest groups sign-ins by flow kind and this sentence, so each
/// sentence is a fixed shape: the variable parts are the provider's own words, a package or a number.
/// </summary>
public static class SignInDiagnosis
{
    public static string Explain(SignInTrail trail)
    {
        var steps = trail.Steps;
        SignInStep? Last(SignInStepKind kind) => steps.LastOrDefault(s => s.Kind == kind);

        if (Last(SignInStepKind.TokenReceived) is { } token)
        {
            var scopes = string.IsNullOrWhiteSpace(token.Outcome) ? "no scopes named" : string.Join(", ", token.Outcome.Split(' '));
            return $"Signed in: a token came back with {scopes}.";
        }
        if (Last(SignInStepKind.NoBrowser) is { } noBrowser)
        {
            return $"No browser could open the sign-in page: {Sentence(noBrowser.Detail)}";
        }
        if (Last(SignInStepKind.BrowserRefused) is { } refused)
        {
            return $"The sign-in page was never opened: {Sentence(refused.Detail)}";
        }
        if (steps.LastOrDefault(s => s.Kind == SignInStepKind.Hop && s.Outcome == "blocked") is { } blocked)
        {
            return $"Redirect reached Flyleaf's browser and was blocked: {Sentence(blocked.Detail["blocked: ".Length..])}";
        }
        if (Last(SignInStepKind.ProviderError) is { } provider)
        {
            return $"The provider refused: {Sentence(provider.Detail)}";
        }
        if (Last(SignInStepKind.StateMismatch) is not null)
        {
            return "A redirect came back for a different sign-in (state did not match); this one was not completed.";
        }
        if (Last(SignInStepKind.StaleRedirect) is not null)
        {
            return "A redirect arrived from an earlier, abandoned sign-in.";
        }
        if (Last(SignInStepKind.ExchangeFailed) is { } exchange)
        {
            return $"The redirect came back, but exchanging the code for a token failed: HTTP {exchange.Outcome}.";
        }
        if (steps.Any(s => s.Kind == SignInStepKind.AccountPicker && s.Outcome == "cancelled"))
        {
            return "Google account picker cancelled.";
        }
        if (Last(SignInStepKind.ConsentDenied) is { } denied)
        {
            return $"The consent screen was declined ({denied.Detail}).";
        }
        if (Last(SignInStepKind.NoReturn) is { } noReturn)
        {
            return ExplainNoReturn(trail, noReturn);
        }
        return "The sign-in is still in progress.";
    }

    private static string ExplainNoReturn(SignInTrail trail, SignInStep noReturn)
    {
        var steps = trail.Steps;
        if (steps.Any(s => s.Kind == SignInStepKind.Returned))
        {
            return "The redirect came back, but no token followed.";
        }
        if (steps.LastOrDefault(s => s.Kind == SignInStepKind.Hop && s.Outcome == "handed") is { } hop)
        {
            return $"Flyleaf handed the redirect to {hop.Detail["handed back to ".Length..]}, but it never reached the app.";
        }
        if (steps.LastOrDefault(s => s.Kind == SignInStepKind.RecoverableAuth) is { } recoverable)
        {
            return $"Google asked for the user to act ({recoverable.Detail}), and it was not completed.";
        }
        if (steps.Any(s => s.Kind == SignInStepKind.AccountPicker && s.Outcome == "chosen"))
        {
            return "The Google account was chosen, but the consent step never answered.";
        }
        if (steps.LastOrDefault(s => s.Kind == SignInStepKind.LeftApp) is { } left)
        {
            var how = noReturn.Outcome == "resumed"
                ? string.Create(CultureInfo.InvariantCulture, $"came back by Back after {(int)(noReturn.At - left.At).TotalSeconds} s")
                : "nothing came back within 10 minutes";
            return $"Opened the {trail.FlowKind} page; no redirect ever reached Flyleaf or the app; {how}. "
                + "The provider page most likely refused the request before redirecting (scope or redirect URI not registered).";
        }
        return "The sign-in never left the app, and nothing came back.";
    }

    private static string Sentence(string text)
    {
        var trimmed = text.Trim();
        return trimmed.EndsWith('.') ? trimmed : trimmed + ".";
    }
}
