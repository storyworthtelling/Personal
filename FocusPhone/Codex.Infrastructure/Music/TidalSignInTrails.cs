using Codex.Domain.Music;
using Storyworthtelling.Diagnostics;

namespace Codex.Infrastructure.Music;

/// <summary>
/// The TIDAL sign-in's trail (LOG-LOOP-PLAN.md §4.2): every way the PKCE flow and the device flow end, as a
/// <see cref="SignInTrail"/> step, so a sign-in that never comes back — the Error 1002 class, TIDAL's page refusing
/// before any redirect — leaves a diagnosis instead of nothing. The PKCE flow id is the OAuth state, which also
/// rides the redirect URL, so Flyleaf's redirect hop and Score's own trail join on it.
/// </summary>
public static class TidalSignInTrails
{
    private static IReadOnlyList<string> Scopes => TidalSignIn.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static SignInTrail StartPkce(string state, string clientId, DateTimeOffset at) =>
        SignInTrail.Start(SignInFlowKinds.TidalPkce, state, at, clientId, TidalSignIn.RedirectUri, Scopes, SignInBrowserPaths.CustomTab);

    public static SignInTrail StartDevice(string deviceCode, string clientId, DateTimeOffset at) =>
        SignInTrail.Start(SignInFlowKinds.TidalDevice, deviceCode, at, clientId, null, Scopes, SignInBrowserPaths.None);

    /// <summary>What the browser handed back, read against the state this screen started.</summary>
    public static SignInTrail Redirect(SignInTrail trail, string redirect, OAuthRedirectResult result, DateTimeOffset at) => result.Status switch
    {
        OAuthRedirectStatus.Authorized => trail.Returned(at, redirect),
        OAuthRedirectStatus.Declined => trail.Returned(at, redirect).ProviderError(at, result.Error ?? "access_denied", null),
        OAuthRedirectStatus.StateMismatch => trail.Returned(at, redirect).StateMismatch(at, trail.FlowId, StateOf(redirect) ?? "(none)"),
        _ => trail.Returned(at, redirect).ProviderError(at, "unusable redirect", result.Error),
    };

    /// <summary>A redirect that arrived with no sign-in waiting for it.</summary>
    public static SignInTrail Stale(string redirect, DateTimeOffset at)
    {
        var state = StateOf(redirect) ?? SignInTrail.NewFlowId();
        return SignInTrail.Start(SignInFlowKinds.TidalPkce, state, at, null, TidalSignIn.RedirectUri, Scopes, SignInBrowserPaths.CustomTab)
            .Returned(at, redirect)
            .StaleRedirect(at, state);
    }

    /// <summary>The code-for-token exchange's answer. A success records the token's scopes and expiry, never the token.</summary>
    public static SignInTrail Exchange(SignInTrail trail, TidalTokenPollResult result, DateTimeOffset at) =>
        result.Status == TidalTokenPollStatus.Success && !string.IsNullOrEmpty(result.RefreshToken)
            ? trail.TokenReceived(at, Scopes, result.ExpiresIn > 0 ? at.AddSeconds(result.ExpiresIn) : null, "refresh token")
            : trail.ExchangeFailed(at, 0, string.IsNullOrEmpty(result.Detail) ? result.Status.ToString() : $"{result.Status}: {result.Detail}");

    public static SignInTrail ExchangeThrew(SignInTrail trail, Exception exception, DateTimeOffset at) =>
        trail.ExchangeFailed(at, 0, $"{exception.GetType().Name}: {exception.Message}");

    /// <summary>How a device-code poll loop ended.</summary>
    public static SignInTrail DevicePoll(SignInTrail trail, TidalTokenPollResult result, DateTimeOffset at) => result.Status switch
    {
        TidalTokenPollStatus.Success => trail.TokenReceived(at, Scopes, result.ExpiresIn > 0 ? at.AddSeconds(result.ExpiresIn) : null, "refresh token"),
        TidalTokenPollStatus.Denied => trail.ProviderError(at, "access_denied", result.Detail),
        TidalTokenPollStatus.Expired => trail.TimedOut(at, "the device code's window closed before it was approved"),
        _ => trail.ProviderError(at, result.Status.ToString(), result.Detail),
    };

    private static string? StateOf(string redirect)
    {
        // Read off the string itself: System.Uri leaves Query empty for some custom schemes.
        var start = redirect.IndexOf('?');
        if (start < 0)
        {
            return null;
        }
        var end = redirect.IndexOf('#', start);
        var query = end < 0 ? redirect[(start + 1)..] : redirect[(start + 1)..end];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "state")
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }
        return null;
    }
}
