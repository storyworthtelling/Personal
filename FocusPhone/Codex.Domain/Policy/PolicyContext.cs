namespace Codex.Domain.Policy;

/// <summary>
/// Everything the policy plan depends on, gathered by the Android layer:
/// which packages we ship, which look like browsers or launchers, and the
/// persistent blocklist.
/// </summary>
public record PolicyContext(
    IReadOnlySet<string> OwnPackages,
    IReadOnlySet<string> BrowserPackages,
    IReadOnlySet<string> OtherHomePackages,
    IReadOnlySet<string> BlockedPackages,
    bool DebuggingLockedDown)
{
    // Every first-party app the owner ships lives under this namespace. Anything
    // matching it is known-good: the categorizer clears it outright so a
    // sideloaded reinstall — which always reports an Unknown Play rating — can
    // never re-quarantine it. An explicit block still wins and stays reversible
    // from admin mode. The trailing dot keeps look-alike namespaces out.
    public const string OwnerPackagePrefix = "com.storyworthtelling.";

    public const string LauncherPackage = "com.storyworthtelling.focusphone.launcher";
    public const string CommunicationPackage = "com.storyworthtelling.focusphone.communication";
    public const string CameraPackage = "com.storyworthtelling.focusphone.camera";
    public const string AudioPackage = "com.storyworthtelling.focusphone.audio";
    public const string PlayStorePackage = "com.android.vending";

    // The two hard-restricted permissions the Communication app needs to read
    // the system text and call-log stores. These are the canonical Android
    // permission strings (matching Android.Manifest.Permission.ReadSms /
    // ReadCallLog), kept here so the platform-agnostic Domain can name them in
    // the policy plan without an Android reference.
    public const string ReadSmsPermission = "android.permission.READ_SMS";
    public const string ReadCallLogPermission = "android.permission.READ_CALL_LOG";

    // Write permissions the unified inbox needs to delete a call/voicemail from
    // the call log and to save/link an unknown sender into Contacts. WRITE_CALL_LOG
    // is hard-restricted (whitelisted like READ_CALL_LOG); WRITE_CONTACTS is a
    // normal dangerous permission the device owner can grant outright.
    public const string WriteCallLogPermission = "android.permission.WRITE_CALL_LOG";
    public const string WriteContactsPermission = "android.permission.WRITE_CONTACTS";

    public static readonly IReadOnlySet<string> FocusPhonePackages = new HashSet<string>
    {
        LauncherPackage,
        CommunicationPackage,
        CameraPackage,
        AudioPackage,
    };

    // Third-party apps the owner keeps through the cutover. None is first-party,
    // so the fail-closed categorizer would quarantine them and the policy plan
    // would suspend the ones that look like browsers — leaving the owner staring
    // at the managed-device "access denied by your workplace" wall. Each is
    // cleared by the categorizer and explicitly kept runnable by the policy plan.
    // (Package ids: GitHub, Gemini — still the Bard id — Google Maps, reMarkable.)
    public const string GitHubPackage = "com.github.android";
    public const string GeminiPackage = "com.google.android.apps.bard";
    public const string MapsPackage = "com.google.android.apps.maps";
    public const string RemarkablePackage = "com.remarkable.mobile";

    public static readonly IReadOnlySet<string> AllowlistedPackages = new HashSet<string>
    {
        GitHubPackage,
        GeminiPackage,
        MapsPackage,
        RemarkablePackage,
    };

    // The system browser, kept runnable for ONE reason: those allowlisted apps
    // sign in through an OAuth Chrome Custom Tab, which a locked same-domain
    // WebView cannot carry (the flow redirects across domains). It is never
    // surfaced in the launcher tray (the tray filters out browsers) and never
    // becomes the default web handler — RestrictedBrowserActivity stays pinned
    // for ordinary links — so it can't be used for free browsing. Apps reach it
    // only by launching a Custom Tab with the package set explicitly, which
    // bypasses the pinned handler.
    public const string LoginBrowserPackage = "com.android.chrome";

    public static readonly IReadOnlySet<string> LoginBrowserPackages = new HashSet<string>
    {
        LoginBrowserPackage,
    };

    // Chrome's enterprise URL filter, pushed as a managed configuration so the
    // login browser is not an open door. Everything is blocked (URLBlocklist =
    // "*") except these identity-provider hosts, which is what closes the two
    // escape paths a bare un-suspend leaves open: the Custom Tab's "Open in
    // Chrome" overflow item drops into full Chrome, and links on a sign-in page
    // would otherwise reach the open web — both now land on Chrome's
    // "blocked by your administrator" page. The filter matches a host and its
    // subdomains. This is the one part likely to need on-device tuning: too tight
    // and a provider's consent screen fails to load (add its asset host); the
    // trade-off is kept deliberately narrow (no bare google.com) so search and
    // general browsing stay closed.
    public static readonly IReadOnlyList<string> LoginBrowserUrlAllowlist =
    [
        // Google account consent (covers Gemini and Maps sign-in) + its assets.
        "accounts.google.com",
        "accounts.youtube.com",
        "gstatic.com",
        "googleusercontent.com",
        // GitHub OAuth + its asset and avatar hosts.
        "github.com",
        "githubassets.com",
        "githubusercontent.com",
        // reMarkable account.
        "remarkable.com",
    ];
}
