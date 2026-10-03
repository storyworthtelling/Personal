namespace Index.Domain.Connect;

/// <summary>
/// The facts the connect page decides on, kept apart from the Android calls that read them.
///
/// <para>Plaid's sign-in runs in real Chrome, not an embedded web view: large national banks fingerprint
/// the browser environment and refuse to render their sign-in inside one. On the owner's FocusPhone the
/// device-owner policy suspends every real browser except while Admin mode is open, and pins Flyleaf's
/// own single-domain viewer as the default http/https handler. That second fact is why the old check
/// failed: the default browser stays the restricted viewer <b>even with Admin mode open</b>, so asking
/// "which app is the default browser?" cannot tell the two states apart (R3, D132/D133).</para>
///
/// <para>The question that can is "is Chrome suspended?" — <see cref="DecideAdminMode"/> takes that
/// answer and whether Chrome is installed at all. Reading them is Android's job (PackageManager); the
/// decision itself is pure and tested. How the real flag behaves under the lockdown is unverified until
/// it is run on the device.</para>
/// </summary>
public static class ConnectBrowserPolicy
{
    /// <summary>
    /// The FocusPhone launcher package. Its <c>RestrictedBrowserActivity</c> is the device's pinned
    /// default browser after the cutover — a single-domain WebView, not a general-purpose browser.
    /// </summary>
    public const string FocusPhoneRestrictedBrowserPackage = "com.storyworthtelling.focusphone.launcher";

    /// <summary>The stock Chrome package, the browser Plaid's sign-in is sent to (and the manifest's
    /// <c>&lt;queries&gt;</c> entry that makes it visible on Android 11+).</summary>
    public const string ChromePackage = "com.android.chrome";

    /// <summary>
    /// Admin mode, as seen through Chrome: absent Chrome is its own state, otherwise Chrome being
    /// suspended means Admin mode is closed and Chrome being runnable means it is open. Only booleans
    /// come in, so the default browser cannot influence the answer.
    /// </summary>
    public static AdminModeState DecideAdminMode(bool chromeInstalled, bool chromeSuspended) =>
        !chromeInstalled ? AdminModeState.ChromeMissing
        : chromeSuspended ? AdminModeState.Needed
        : AdminModeState.Open;

    /// <summary>
    /// True only when the resolved default-browser package is FocusPhone's restricted viewer. A null or
    /// empty package (no resolvable default browser — e.g. package visibility withheld it) is treated as
    /// "not restricted" on purpose, so a browser-capable phone is never falsely blocked; the guard fires
    /// only on a positive identification of the viewer that genuinely cannot complete a connection.
    /// This no longer gates the connect page (see the class remarks) — it only identifies the viewer.
    /// </summary>
    public static bool IsRestrictedViewer(string? defaultBrowserPackage) =>
        string.Equals(defaultBrowserPackage, FocusPhoneRestrictedBrowserPackage, StringComparison.Ordinal);
}
