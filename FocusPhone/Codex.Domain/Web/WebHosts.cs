namespace Codex.Domain.Web;

/// <summary>
/// Host tables for the restricted browser's navigation gate: the Google auth hosts a login flow
/// is allowed to cross to (federated sign-in is Google-only), and the social registrable domains
/// that are hard-blocked in-app (adult content is blocked network-wide by the filtering DNS, which
/// is unbounded; the browser adds this explicit social block on top). Pure so both are unit-tested.
/// </summary>
public static class WebHosts
{
    // A login Custom Tab / redirect may cross to these regardless of the originating domain, so a
    // "Sign in with Google" flow completes without letting general cross-domain browsing through.
    private static readonly string[] GoogleAuthHostSuffixes =
    [
        "accounts.google.com",
        "accounts.youtube.com",
        "oauth2.googleapis.com",
        "gstatic.com",
        "googleusercontent.com",
        "ssl.gstatic.com",
    ];

    // The Play Store's website. Matched by host rather than registrable domain: Google is one
    // registrable domain, so a domain block would close Maps, Search and Gmail along with it.
    // The Play Store app keeps running (it delivers the owner's app updates); only its web face is shut.
    private static readonly string[] PlayStoreHostSuffixes =
    [
        "play.google.com",
        "play.app.goo.gl",
        "market.android.com",
    ];

    /// <summary>Social-media registrable domains blocked in the restricted browser (eTLD+1 match).</summary>
    public static IReadOnlySet<string> BlockedRegistrableDomains { get; } = new HashSet<string>
    {
        "facebook.com", "fb.com", "instagram.com", "twitter.com", "x.com", "tiktok.com",
        "reddit.com", "snapchat.com", "pinterest.com", "tumblr.com",
    };

    /// <summary>True if the host is (or is a subdomain of) the Play Store's website.</summary>
    public static bool IsPlayStoreHost(string host) =>
        PlayStoreHostSuffixes.Any(suffix =>
            host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>True if the host is (or is a subdomain of) a Google auth host a login may cross to.</summary>
    public static bool IsGoogleAuthHost(string host) =>
        GoogleAuthHostSuffixes.Any(suffix =>
            host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase));
}
