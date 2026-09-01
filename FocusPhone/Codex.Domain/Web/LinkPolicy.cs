namespace Codex.Domain.Web;

/// <summary>
/// The restricted browser's navigation gate. A target is allowed when it is plain http/https and
/// stays on the originating link's registrable domain (eTLD+1) — so a link opens but you can't
/// browse away from its site. Two carve-outs on top: a <b>Google auth host</b> is always allowed
/// (so a "Sign in with Google" redirect completes across domains), and a <b>social/adult host</b>
/// is always blocked (even same-domain). Everything else off-domain, and any non-web scheme, is
/// blocked. Adult content is also blocked network-wide by the filtering DNS.
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

        var targetDomain = publicSuffixList.GetRegistrableDomain(target.Host);

        // Social/adult hosts are blocked even on the same site.
        if (targetDomain is not null && WebHosts.BlockedRegistrableDomains.Contains(targetDomain))
        {
            return false;
        }
        // A Google sign-in redirect is allowed to cross domains so app logins complete.
        if (WebHosts.IsGoogleAuthHost(target.Host))
        {
            return true;
        }

        // Otherwise stay on the originating site.
        var originDomain = publicSuffixList.GetRegistrableDomain(origin.Host);
        return originDomain is not null && originDomain == targetDomain;
    }

    private static bool IsWebScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}
