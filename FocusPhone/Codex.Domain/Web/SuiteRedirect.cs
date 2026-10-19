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

    /// <summary>
    /// A third-party app's own OAuth callback that is also handed on, aimed at that app's package
    /// alone so no other app registered for the scheme can take the code. Each entry names the
    /// scheme, the host and the path the callback lives at; anything else on that scheme stays blocked.
    /// </summary>
    private static readonly (string Scheme, string Host, string Path, string Package)[] AppCallbacks =
    [
        ("cfa", "order.chick-fil-a.com", "/oauth2/callback", "com.chickfila.cfaflagship"),
    ];

    public static string? AppRedirectPackage(Uri target)
    {
        foreach (var callback in AppCallbacks)
        {
            if (target.Scheme == callback.Scheme
                && string.Equals(target.Host, callback.Host, StringComparison.OrdinalIgnoreCase)
                && target.AbsolutePath == callback.Path)
            {
                return callback.Package;
            }
        }

        return null;
    }
}
