using System.Security.Cryptography;
using System.Text;

namespace Codex.Domain.Music;

/// <summary>
/// The pair PKCE is built on: a secret kept on the phone, and the hash of it that
/// travels (RFC 7636).
///
/// It is what makes a browser sign-in safe for an app that cannot keep a secret in
/// its redirect. The authorization code comes back through a URL any app on the
/// device could in principle receive; without the verifier — which never leaves —
/// that code cannot be exchanged for anything.
/// </summary>
public sealed record PkceCodes(string Verifier, string Challenge)
{
    /// <summary>A fresh pair. Never reuse one: the verifier is the whole security of the flow.</summary>
    public static PkceCodes Create()
    {
        // 32 bytes is 43 base64url characters, which is exactly the spec's minimum
        // length and its recommended entropy in the same number.
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new PkceCodes(verifier, ChallengeFor(verifier));
    }

    /// <summary>The S256 challenge for a verifier: base64url of its SHA-256, unpadded.</summary>
    public static string ChallengeFor(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    // Base64 with the URL alphabet and no padding — the encoding the spec names, and
    // the one that survives being a query parameter without escaping.
    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Signing in to TIDAL with authorization code + PKCE — the browser flow.
///
/// Score was built on the device-code flow, which is the shape a locked-down phone
/// wants: a code on the screen, approved elsewhere, no browser. TIDAL refuses it —
/// "Client is not a Limited Input Device client" — because their own SDK documents
/// that flow as available to TIDAL's internal apps only. This is the flow they do
/// offer, and the phone can carry it: the launcher un-suspends the system browser
/// for OAuth Custom Tabs against an allowlist of identity hosts, which is the same
/// door the GitHub sign-in goes through.
/// </summary>
public static class TidalSignIn
{
    /// <summary>Where the owner signs in. TIDAL's login host, not its API host.</summary>
    public const string AuthorizeEndpoint = "https://login.tidal.com/authorize";

    /// <summary>
    /// The redirect the browser hands back to. A scheme named for the app, per
    /// RFC 8252 for native apps — and it has to be registered against the client in
    /// TIDAL's developer portal or the authorize call is refused before it is shown.
    /// </summary>
    public const string RedirectUri = "com.storyworthtelling.focusphone.audio://tidal-auth";

    /// <summary>
    /// What Score asks the account for: read and write the owner's own data, and
    /// play against their subscription rather than only reading the catalogue.
    /// </summary>
    public const string Scope = "r_usr w_usr w_sub";

    /// <summary>The URL to open in the browser.</summary>
    public static string AuthorizeUrl(string clientId, string redirectUri, string challenge, string state)
    {
        var query = string.Join('&',
            "response_type=code",
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"scope={Uri.EscapeDataString(Scope)}",
            $"code_challenge_method=S256",
            $"code_challenge={Uri.EscapeDataString(challenge)}",
            $"state={Uri.EscapeDataString(state)}");

        return $"{AuthorizeEndpoint}?{query}";
    }

    /// <summary>
    /// A one-off value echoed back through the redirect, so a code arriving from
    /// somewhere other than the sign-in we started can be told apart from ours.
    /// </summary>
    public static string NewState() => PkceCodes.Base64Url(RandomNumberGenerator.GetBytes(16));
}

public enum OAuthRedirectStatus
{
    /// <summary>A code came back, and it belongs to the sign-in we started.</summary>
    Authorized,

    /// <summary>The owner said no. Not a failure to report — a decision to respect.</summary>
    Declined,

    /// <summary>A code arrived carrying a state that is not ours. Refuse it.</summary>
    StateMismatch,

    /// <summary>Anything else, including a redirect that carries neither code nor error.</summary>
    Failed,
}

public sealed record OAuthRedirectResult(OAuthRedirectStatus Status, string? Code = null, string? Error = null);

/// <summary>
/// Reading what the browser handed back.
///
/// Pure because it is the security-carrying half of the flow and it is all string
/// handling: the state check is what stops a code from another sign-in — or from
/// nowhere — being exchanged as if it were ours, and a check like that is worth
/// being able to test without a browser.
/// </summary>
public static class OAuthRedirect
{
    public static OAuthRedirectResult Read(string? redirect, string expectedState)
    {
        if (string.IsNullOrWhiteSpace(redirect) || !Uri.TryCreate(redirect, UriKind.Absolute, out var uri))
        {
            return new OAuthRedirectResult(OAuthRedirectStatus.Failed, Error: "The sign-in did not come back with a result.");
        }

        var query = Parse(uri);
        query.TryGetValue("state", out var state);

        if (query.TryGetValue("error", out var error))
        {
            query.TryGetValue("error_description", out var description);
            var detail = string.IsNullOrWhiteSpace(description) ? error : $"{error} — {description}";

            // access_denied is the owner declining, which is not something to report
            // as a fault; everything else is.
            return new OAuthRedirectResult(
                error == "access_denied" ? OAuthRedirectStatus.Declined : OAuthRedirectStatus.Failed,
                Error: detail);
        }

        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            return new OAuthRedirectResult(OAuthRedirectStatus.Failed, Error: "The sign-in came back without a code.");
        }

        // Checked after the code is found and before it is handed on: a code with the
        // wrong state is the case this exists for, and it must never be exchanged.
        if (state != expectedState)
        {
            return new OAuthRedirectResult(
                OAuthRedirectStatus.StateMismatch,
                Error: "The sign-in that came back is not the one that was started.");
        }

        return new OAuthRedirectResult(OAuthRedirectStatus.Authorized, code);
    }

    private static Dictionary<string, string> Parse(Uri uri)
    {
        // A custom-scheme redirect is opaque to Uri.Query on some shapes, so take
        // whatever follows the first '?' rather than trusting the parse.
        var text = uri.OriginalString;
        var mark = text.IndexOf('?');
        var query = mark < 0 ? string.Empty : text[(mark + 1)..];

        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.Split('=', 2);
            pairs[Uri.UnescapeDataString(split[0])] =
                split.Length > 1 ? Uri.UnescapeDataString(split[1].Replace('+', ' ')) : string.Empty;
        }

        return pairs;
    }
}
