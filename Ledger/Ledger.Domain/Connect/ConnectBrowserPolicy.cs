namespace Ledger.Domain.Connect;

/// <summary>
/// Decides whether the phone can actually complete a Plaid Hosted Link connection in a browser.
///
/// Plaid's hosted Link page is opened in the system browser (see the Android PlaidLinkPage), because OAuth
/// banks such as Chase fingerprint the browser environment and refuse to render their sign-in inside an
/// embedded WebView. On the owner's FocusPhone, however, the device-owner policy suspends every real
/// browser and pins FocusPhone's own <c>RestrictedBrowserActivity</c> — a single-domain WebView living in
/// the launcher package — as the forced default http/https handler. Opening the Link URL there dead-ends:
/// it's a WebView (so Chase blocks it) and it's confined to the originating domain (so the Plaid → bank
/// OAuth redirect is refused). When that viewer is the default browser, the connect page should send the
/// owner to link on a browser-capable phone — Ledger's vault backup then restores the access token here,
/// and the daily balance refresh needs no browser at all.
/// </summary>
public static class ConnectBrowserPolicy
{
    /// <summary>
    /// The FocusPhone launcher package. Its <c>RestrictedBrowserActivity</c> is the device's pinned
    /// default browser after the cutover — a single-domain WebView, not a general-purpose browser.
    /// </summary>
    public const string FocusPhoneRestrictedBrowserPackage = "com.storyworthtelling.focusphone.launcher";

    /// <summary>
    /// True only when the resolved default-browser package is FocusPhone's restricted viewer. A null or
    /// empty package (no resolvable default browser — e.g. package visibility withheld it) is treated as
    /// "not restricted" on purpose, so a browser-capable phone is never falsely blocked; the guard fires
    /// only on a positive identification of the viewer that genuinely cannot complete a connection.
    /// </summary>
    public static bool IsRestrictedViewer(string? defaultBrowserPackage) =>
        string.Equals(defaultBrowserPackage, FocusPhoneRestrictedBrowserPackage, StringComparison.Ordinal);
}
