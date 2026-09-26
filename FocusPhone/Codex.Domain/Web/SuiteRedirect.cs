using Codex.Domain.Policy;

namespace Codex.Domain.Web;

/// <summary>
/// The one kind of non-web link the restricted browser hands on rather than blocking: a
/// suite app's own OAuth redirect, such as Score's
/// <c>com.storyworthtelling.focusphone.audio://tidal-auth?code=…</c>.
///
/// <see cref="LinkPolicy"/> refuses every non-web scheme, which is right for a page trying
/// to open another app — but a sign-in that opens in this browser ends by redirecting to
/// the app that started it, and blocking that made TIDAL's sign-in a dead end outside
/// Admin mode. The scheme is matched against the owner namespace followed by a dot, so
/// only the suite's own reverse-domain schemes pass: not <c>intent:</c>, not <c>market:</c>,
/// not another app's scheme, and not a look-alike prefix.
/// </summary>
public static class SuiteRedirect
{
    public static bool IsSuiteRedirect(Uri target) =>
        target.Scheme != Uri.UriSchemeHttp
        && target.Scheme != Uri.UriSchemeHttps
        && target.Scheme.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal)
        && target.Scheme.Length > PolicyContext.OwnerPackagePrefix.Length;
}
