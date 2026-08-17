namespace Codex.Domain.Web;

/// <summary>
/// The restricted browser's navigation gate: a target is allowed only when it is
/// plain http/https and shares the originating link's registrable domain (eTLD+1).
/// Everything else — cross-domain navigations, redirects landing off-domain, and
/// non-web schemes — is blocked.
/// </summary>
public class LinkPolicy
{
    private readonly PublicSuffixList publicSuffixList;

    public LinkPolicy(PublicSuffixList publicSuffixList)
    {
        this.publicSuffixList = publicSuffixList;
    }

    public static LinkPolicy CreateDefault() => new(PublicSuffixList.LoadEmbedded());

    public bool IsNavigationAllowed(Uri origin, Uri target)
    {
        if (!IsWebScheme(origin) || !IsWebScheme(target))
        {
            return false;
        }
        if (origin.Host.Length == 0 || target.Host.Length == 0)
        {
            return false;
        }

        var originDomain = publicSuffixList.GetRegistrableDomain(origin.Host);
        var targetDomain = publicSuffixList.GetRegistrableDomain(target.Host);
        return originDomain is not null && originDomain == targetDomain;
    }

    private static bool IsWebScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}
