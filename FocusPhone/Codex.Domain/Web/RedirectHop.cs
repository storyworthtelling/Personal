using Storyworthtelling.Diagnostics;

namespace Codex.Domain.Web;

/// <summary>
/// Flyleaf's half of a sign-in that runs in its restricted browser (LOG-LOOP-PLAN.md §4.2): the redirect passed
/// through on its way back to the app that asked, or was blocked. The trail is keyed by the redirect's OAuth
/// <c>state</c>, which the app's own trail is keyed by too, so the reader can lay the two side by side. A
/// standing credential in the redirect is withheld by <see cref="SignInTrail"/> before it is stored.
/// </summary>
public static class RedirectHop
{
    /// <summary>The flow id of a redirect that carried no state, so the hop still has a name.</summary>
    public const string NoState = "no-state";

    private static readonly string[] AnswerParameters = ["code", "state", "error", "access_token", "id_token"];

    /// <summary>Whether a URL looks like an authorization answer rather than an ordinary page.</summary>
    public static bool IsSignInAnswer(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Parameters(target).Any(p => AnswerParameters.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
    }

    public static SignInTrail Handed(Uri redirect, string? package, DateTimeOffset at) =>
        Start(redirect, at).Hop(at, package ?? "(unknown)", null);

    public static SignInTrail Blocked(Uri target, string reason, DateTimeOffset at) =>
        Start(target, at).Hop(at, null, reason);

    private static SignInTrail Start(Uri redirect, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(redirect);
        var state = Parameters(redirect).FirstOrDefault(p => string.Equals(p.Name, "state", StringComparison.OrdinalIgnoreCase)).Value;
        var flowId = string.IsNullOrEmpty(state) ? NoState : state;
        var where = redirect.IsAbsoluteUri ? $"{redirect.Scheme}://{redirect.Authority}{redirect.AbsolutePath}" : "(relative)";
        return SignInTrail.Start(SignInFlowKinds.RedirectHop, flowId, at, null, where, [], SignInBrowserPaths.RestrictedBrowser)
            .Returned(at, redirect.OriginalString);
    }

    // Query and fragment both: an implicit-grant answer arrives in the fragment.
    private static IEnumerable<(string Name, string Value)> Parameters(Uri target)
    {
        var text = target.OriginalString;
        var start = text.IndexOfAny(['?', '#']);
        if (start < 0)
        {
            yield break;
        }
        foreach (var pair in text[(start + 1)..].Split(['&', '#', '?'], StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            yield return equals < 0
                ? (pair, string.Empty)
                : (pair[..equals], Uri.UnescapeDataString(pair[(equals + 1)..]));
        }
    }
}
