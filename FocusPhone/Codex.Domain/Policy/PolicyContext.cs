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
    bool DebuggingLockedDown,
    // The never-visible categories (games / social / dating / gambling / short-form), hard-suspended
    // and not reversible in Admin — computed by the head via HardBlock.Detect over installed apps.
    IReadOnlySet<string> NeverVisiblePackages,
    // Whether the owner's 6-hour-delayed Admin session (UnlockStateMachine.UnlockState.AdminActive)
    // is currently open. The login browser (see LoginBrowserPackage below) is gated on this rather
    // than kept always-runnable: an allowlisted app reaching Chrome for an ordinary in-app link
    // (Maps opening a business's website, say) is free general browsing, not a sign-in, and the
    // fix is to make Chrome unavailable outside the owner-initiated Admin window entirely.
    bool AdminModeActive)
{
    // Every first-party app the owner ships lives under this namespace. Anything
    // matching it is known-good: the categorizer clears it outright so a
    // sideloaded reinstall — which always reports an Unknown Play rating — can
    // never re-quarantine it. An explicit block still wins and stays reversible
    // from admin mode. The trailing dot keeps look-alike namespaces out.
    public const string OwnerPackagePrefix = "com.storyworthtelling.";

    public const string LauncherPackage = "com.storyworthtelling.focusphone.launcher";
    public const string CommunicationPackage = "com.storyworthtelling.focusphone.communication";
    // Frozen: the head is renamed to Plate IN PLACE, so the package id never moves.
    public const string PlatePackage = "com.storyworthtelling.focusphone.camera";
    public const string AudioPackage = "com.storyworthtelling.focusphone.audio";

    // Foreword, the morning hub. It is a sibling repo app rather than a FocusPhone head, so it
    // carries no "focusphone" segment — and it still says "dailyloader" because renaming a package
    // presents the app to the update channel as something new. It is named here because the shelf
    // draws it: without an entry it fell back to the launcher icon, which Android masks round.
    public const string ForewordPackage = "com.storyworthtelling.dailyloader";

    // Index, the household money app (the former Ledger, renamed in place: it keeps the
    // "com.storyworthtelling.ledger" id so a phone upgrades it rather than installing a second app).
    // Like Foreword it is a sibling repo app rather than a FocusPhone head. It is named here because
    // the shelf draws it cover-out; the sibling-book test itself is the owner prefix above, which
    // already covers it.
    public const string IndexPackage = "com.storyworthtelling.ledger";

    // Folio, the manuscript-review app. Its package still says "storyboarder" — Canonizer's id,
    // kept so installed copies upgrade in place. Named here because the shelf draws it cover-out.
    public const string FolioPackage = "com.storyworthtelling.storyboarder";

    // Pulp, the household's TV and film guide (P12). Frozen from its first build (PulpIdentity in
    // Pulp.Domain holds the same value). Named here because the shelf draws it cover-out.
    public const string PulpPackage = "com.storyworthtelling.pulp";

    public const string PlayStorePackage = "com.android.vending";

    // A protected system package: DevicePolicyManager.SetPackagesSuspended refuses to suspend
    // it and reports the attempt as a failure every single policy pass. It can surface in the
    // browser/home-resolving package queries on some OEM builds (e.g. a Settings deep-link
    // handler), so it is excluded from the suspend candidate set the same way the Play Store
    // is, rather than logging the same permanent, expected failure forever.
    public const string SettingsPackage = "com.android.settings";

    // Google Drive. Not first-party and not allowlisted for the owner to browse — it is here
    // only as a share target: Plate hands a finished scan to it rather than acquiring a Drive
    // API client and a write scope of its own.
    public const string DrivePackage = "com.google.android.apps.docs";

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
        PlatePackage,
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

    // The system browser. Made runnable for ONE reason: those allowlisted apps sign in through an
    // OAuth Chrome Custom Tab, which a locked same-domain WebView cannot carry (the flow redirects
    // across domains). It is never surfaced in the launcher tray (the tray filters out browsers) and
    // never becomes the default web handler — RestrictedBrowserActivity stays pinned for ordinary
    // links. That alone is not enough to keep it sign-in-only, though: once un-suspended, an
    // allowlisted app can launch a Custom Tab pointed at any URL, not just its auth host (this is how
    // Maps was observed opening arbitrary business-website links in full, unrestricted Chrome). So
    // PolicyPlanBuilder only un-suspends it while PolicyContext.AdminModeActive is true — the owner's
    // deliberate, time-boxed Admin session — rather than for the lifetime of the allowlisted apps.
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
        // TIDAL sign-in. login.tidal.com is the page the owner types a password into;
        // auth.tidal.com issues the tokens. Score has to use this browser flow because
        // TIDAL reserves its device-code flow for its own apps.
        "tidal.com",
        // ...and the redirect back to Score once it has. The filter applies to every scheme,
        // so under URLBlocklist "*" a successful sign-in would otherwise end on Chrome's
        // "blocked by your administrator" page instead of in Score. Must match
        // TidalSignIn.RedirectUri's scheme (guarded in TidalSignInReachabilityTests).
        "com.storyworthtelling.focusphone.audio://*",
    ];
}
