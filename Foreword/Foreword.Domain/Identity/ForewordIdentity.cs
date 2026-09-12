namespace Foreword.Domain.Identity;

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

    // Added in P9. New keys are allowed; the nine above are what may never move, because a phone is
    // already storing values under them. A new key has nothing to lose — nobody has written to it.
    public const string WorkScheduleJson = "WorkScheduleJson";
    public const string SeenCalendarIds = "SeenCalendarIds";

    /// <summary>
    /// The time the periodic delivery work was actually scheduled for, so moving the delivery time
    /// in code can replace a schedule the phone is already holding. See
    /// <see cref="Foreword.Domain.Delivery.DeliverySchedule"/>.
    /// </summary>
    public const string ScheduledDeliveryTime = "ScheduledDeliveryTime";

    // Google Maps Platform: one key for routes, weather, pollen and air quality, and the house
    // every drive is measured from and every forecast is about.
    public const string MapsApiKey = "MapsApiKey";
    public const string HomeAddress = "HomeAddress";
    public const string HomeLatitude = "HomeLatitude";
    public const string HomeLongitude = "HomeLongitude";

    // Foreword's own tasks — the rules Google Tasks cannot express — and the away pause.
    public const string TasksJson = "TasksJson";
    public const string AwayJson = "AwayJson";

    // Where the brief comes from: the feed list, and how much of the paper it carries.
    public const string FeedsJson = "FeedsJson";
    public const string NewsCount = "NewsCount";
    public const string OpinionCount = "OpinionCount";

    // The highest xkcd number already printed. "Whatever you have not seen" needs a mark.
    public const string SeenComic = "SeenComic";

    // The reading taste profile's local copy. The canonical one is Drive JSON (D29).
    public const string TasteJson = "TasteJson";

    // Whose phone this is, as the word for a PersonColumn — empty means "work it out from the
    // signed-in account". Per-install by definition: it is the one setting that MUST differ between
    // the two phones, which is why it is here and not in the shared calendar-mapping document.
    public const string OwnerColumn = "OwnerColumn";

    // This install's maker's mark, as a base64 PNG (square and small — see MedallionImage).
    public const string OwnerPhoto = "OwnerPhoto";

    // What happened to the prayer document. Kept apart from LastDeliveryOutcome because prayer can
    // fail on its own — it reads a file the rest of the morning does not, behind a passphrase the
    // rest of the morning does not need — and folding it in would make one sentence say two things.
    public const string LastPrayerOutcome = "LastPrayerOutcome";

    /// <summary>
    /// The nine keys a phone was <b>already storing</b> before the rename. These are what may never
    /// move: the guard freezes this list exactly, in order.
    ///
    /// <para>Keys added afterwards are not here and do not need to be. A key nobody has ever
    /// written to has nothing to lose by changing — the freeze is about not orphaning a value that
    /// already exists on a device, not about never adding a setting.</para>
    /// </summary>
    public static IReadOnlyList<string> Frozen { get; } =
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

    /// <summary>Every key this app stores, frozen or not.</summary>
    public static IReadOnlyList<string> All { get; } =
        [
            .. Frozen, WorkScheduleJson, SeenCalendarIds, ScheduledDeliveryTime,
            MapsApiKey, HomeAddress, HomeLatitude, HomeLongitude, TasksJson, AwayJson,
            FeedsJson, NewsCount, OpinionCount, SeenComic, TasteJson, LastPrayerOutcome,
            OwnerColumn, OwnerPhoto,
        ];
}
