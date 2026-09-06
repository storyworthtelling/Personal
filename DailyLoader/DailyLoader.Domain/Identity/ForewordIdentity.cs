namespace DailyLoader.Domain.Identity;

/// <summary>
/// The identifiers that survive the rename, in one place.
///
/// <para>P9 renames DailyLoader to Foreword — folders, namespaces, projects, the on-device
/// label. None of that may move the identity of the <i>installed</i> app, because a phone
/// recognises an update only when the package name is unchanged, and the device-owner update
/// channel installs by package id. So these strings still say <c>dailyloader</c>, and always
/// will. They are the app's address, not a description of what it does.</para>
///
/// <para>They live in Domain because Domain has no dependencies and every layer can see it —
/// including the desktop test build, which is what lets the guards run without an Android
/// workload.</para>
/// </summary>
public static class ForewordIdentity
{
    /// <summary>
    /// Frozen forever. Renaming it would present the app to the update channel as something
    /// new, so the phone would keep the old one installed and never see another update.
    /// </summary>
    public const string ApplicationId = "com.storyworthtelling.dailyloader";

    /// <summary>The name the owner sees on the phone.</summary>
    public const string DisplayName = "Foreword";
}

/// <summary>
/// The nine SharedPreferences keys, as literals.
///
/// <para>They were <c>nameof(TheProperty)</c>, which looks tidy and is a trap: renaming a
/// property renames its key, and nothing reports the loss — the old value is simply not found,
/// and the app comes up as though it had never been configured. The vault passphrase is the
/// worst case, because it is the one secret the vault itself cannot restore.</para>
///
/// <para>The values are exactly the old property names, so every phone already storing them
/// keeps reading the same keys. That is the whole point: this change is invisible on device.</para>
/// </summary>
public static class PreferenceKeys
{
    public const string NytSessionCookie = "NytSessionCookie";
    public const string NytApiKey = "NytApiKey";
    public const string RemarkableDeviceToken = "RemarkableDeviceToken";
    public const string GoogleAccountEmail = "GoogleAccountEmail";
    public const string AnthropicApiKey = "AnthropicApiKey";
    public const string CalendarMappingJson = "CalendarMappingJson";
    public const string VaultPassphrase = "VaultPassphrase";
    public const string LastDeliveryOutcome = "LastDeliveryOutcome";
    public const string LastDeliveryAtUtc = "LastDeliveryAtUtc";

    /// <summary>Every key, in declaration order, so the guard can freeze the whole set.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        NytSessionCookie,
        NytApiKey,
        RemarkableDeviceToken,
        GoogleAccountEmail,
        AnthropicApiKey,
        CalendarMappingJson,
        VaultPassphrase,
        LastDeliveryOutcome,
        LastDeliveryAtUtc,
    ];
}
