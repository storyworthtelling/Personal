namespace Codex.Domain.Apps;

/// <summary>
/// The fixed set of app-tray folders. Textless in the UI (each renders a
/// hand-drawn ink glyph); a folder is hidden whenever no visible app lives in
/// it. There are no user-created folders — this list is the whole taxonomy.
/// </summary>
public enum AppFolder
{
    Finance,
    Travel,
    Shopping,
    Food,
    Health,
    Utilities,
    Productivity,
    Media,
    SmartHome,
}

/// <summary>
/// Folder metadata (display name, ink-glyph drawable, sort order) plus the two
/// automatic placement rules — Android's declared category and a curated map of
/// well-known packages. Kept in the platform-agnostic Domain so the placement
/// logic is unit-tested without an Android dependency; the launcher resolves the
/// <see cref="FolderInfo.IconResourceName"/> against its drawable resources.
/// </summary>
public static class AppFolders
{
    public record FolderInfo(AppFolder Folder, string DisplayName, string IconResourceName, int Order);

    // Android's ApplicationInfo.Category values (stable framework ints). Named
    // here so FromAndroidCategory reads intentionally; the Domain must not
    // reference the Android enum type.
    private const int CategoryAudio = 1;
    private const int CategoryVideo = 2;
    private const int CategoryImage = 3;
    private const int CategoryMaps = 6;
    private const int CategoryProductivity = 7;
    private const int CategoryAccessibility = 8;

    private static readonly IReadOnlyList<FolderInfo> Metadata =
    [
        new(AppFolder.Finance, "Finance", "folder_finance", 0),
        new(AppFolder.Travel, "Travel & Transit", "folder_travel", 1),
        new(AppFolder.Shopping, "Shopping", "folder_shopping", 2),
        new(AppFolder.Food, "Food & Dining", "folder_food", 3),
        new(AppFolder.Health, "Health & Fitness", "folder_health", 4),
        new(AppFolder.Utilities, "Utilities & Tools", "folder_utilities", 5),
        new(AppFolder.Productivity, "Productivity", "folder_productivity", 6),
        new(AppFolder.Media, "Media", "folder_media", 7),
        new(AppFolder.SmartHome, "Smart Home", "folder_smarthome", 8),
    ];

    private static readonly IReadOnlyDictionary<AppFolder, FolderInfo> ByFolder =
        Metadata.ToDictionary(info => info.Folder);

    /// <summary>Folder metadata in display (sort) order.</summary>
    public static IReadOnlyList<FolderInfo> All => Metadata;

    public static FolderInfo Info(AppFolder folder) => ByFolder[folder];

    /// <summary>
    /// The reliable slice of Android's declared category. Only categories that
    /// map unambiguously to one folder are honored; Social, News, Undefined and
    /// Game return null so those apps fall through to loose (or a curated/override
    /// placement).
    /// </summary>
    public static AppFolder? FromAndroidCategory(int androidCategory) => androidCategory switch
    {
        CategoryMaps => AppFolder.Travel,
        CategoryAudio or CategoryVideo or CategoryImage => AppFolder.Media,
        CategoryProductivity => AppFolder.Productivity,
        CategoryAccessibility => AppFolder.Utilities,
        _ => null,
    };

    /// <summary>
    /// Well-known packages that rarely declare a usable Android category, mapped
    /// to their folder. Not exhaustive — anything missing lands loose until the
    /// owner files it, and new entries can be added over time.
    /// </summary>
    public static IReadOnlyDictionary<string, AppFolder> CuratedSeed { get; } = new Dictionary<string, AppFolder>
    {
        // Finance
        ["com.google.android.apps.walletnfcrel"] = AppFolder.Finance,
        ["com.paypal.android.p2pmobile"] = AppFolder.Finance,
        ["com.venmo"] = AppFolder.Finance,
        ["com.chase.sig.android"] = AppFolder.Finance,
        ["com.infonow.bofa"] = AppFolder.Finance,
        ["com.wf.wellsfargomobile"] = AppFolder.Finance,
        ["com.konylabs.capitalone"] = AppFolder.Finance,
        ["com.usaa.mobile.android.usaa"] = AppFolder.Finance,
        ["com.discoverfinancial.mobile"] = AppFolder.Finance,
        ["com.citi.citimobile"] = AppFolder.Finance,
        ["com.americanexpress.android.acctsvcs.us"] = AppFolder.Finance,
        ["com.robinhood.android"] = AppFolder.Finance,
        ["com.coinbase.android"] = AppFolder.Finance,
        ["com.fidelity.android"] = AppFolder.Finance,
        ["com.schwab.mobile"] = AppFolder.Finance,
        ["com.intuit.mint"] = AppFolder.Finance,

        // Travel & Transit
        ["com.ubercab"] = AppFolder.Travel,
        ["com.lyft.android"] = AppFolder.Travel,
        ["com.waze"] = AppFolder.Travel,
        ["com.google.android.apps.maps"] = AppFolder.Travel,
        ["com.tripit"] = AppFolder.Travel,
        ["com.united.mobile.android"] = AppFolder.Travel,
        ["com.aa.android"] = AppFolder.Travel,
        ["com.delta.mobile.android"] = AppFolder.Travel,
        ["com.southwestairlines.mobile"] = AppFolder.Travel,
        ["com.airbnb.android"] = AppFolder.Travel,
        ["com.booking"] = AppFolder.Travel,
        ["com.tripadvisor.tripadvisor"] = AppFolder.Travel,
        ["com.google.android.apps.transit"] = AppFolder.Travel,

        // Shopping
        ["com.amazon.mShop.android.shopping"] = AppFolder.Shopping,
        ["com.ebay.mobile"] = AppFolder.Shopping,
        ["com.walmart.android"] = AppFolder.Shopping,
        ["com.target.ui"] = AppFolder.Shopping,
        ["com.etsy.android"] = AppFolder.Shopping,
        ["com.contextlogic.wish"] = AppFolder.Shopping,
        ["com.bestbuy.android"] = AppFolder.Shopping,
        ["com.thehomedepot"] = AppFolder.Shopping,

        // Food & Dining
        ["com.ubercab.eats"] = AppFolder.Food,
        ["com.dd.doordash"] = AppFolder.Food,
        ["com.grubhub.android"] = AppFolder.Food,
        ["com.yelp.android"] = AppFolder.Food,
        ["com.starbucks.mobilecard"] = AppFolder.Food,
        ["com.mcdonalds.app"] = AppFolder.Food,
        ["com.chipotle.ordering"] = AppFolder.Food,
        ["com.instacart.client"] = AppFolder.Food,
        ["com.kroger.mobile"] = AppFolder.Food,
        ["com.safeway.client.android.safeway"] = AppFolder.Food,

        // Health & Fitness
        ["com.fitbit.FitbitMobile"] = AppFolder.Health,
        ["com.google.android.apps.fitness"] = AppFolder.Health,
        ["com.myfitnesspal.android"] = AppFolder.Health,
        ["com.strava"] = AppFolder.Health,
        ["com.headspace.android"] = AppFolder.Health,
        ["com.calm.android"] = AppFolder.Health,
        ["com.walgreens.android.application"] = AppFolder.Health,
        ["com.cvs.launchers.cvs"] = AppFolder.Health,
        ["epic.mychart.android"] = AppFolder.Health,

        // Utilities & Tools
        ["com.google.android.calculator"] = AppFolder.Utilities,
        ["com.google.android.deskclock"] = AppFolder.Utilities,
        ["com.google.android.apps.nbu.files"] = AppFolder.Utilities,
        ["com.google.android.apps.weather"] = AppFolder.Utilities,
        ["com.google.android.apps.authenticator2"] = AppFolder.Utilities,
        ["com.android.chrome.canary"] = AppFolder.Utilities, // (kept for completeness; browsers are filtered upstream)

        // Productivity
        ["com.google.android.calendar"] = AppFolder.Productivity,
        ["com.google.android.keep"] = AppFolder.Productivity,
        ["com.microsoft.office.outlook"] = AppFolder.Productivity,
        ["com.google.android.apps.docs.editors.docs"] = AppFolder.Productivity,
        ["com.google.android.apps.docs.editors.sheets"] = AppFolder.Productivity,
        ["com.google.android.apps.docs.editors.slides"] = AppFolder.Productivity,
        ["com.todoist"] = AppFolder.Productivity,
        ["com.notion.id"] = AppFolder.Productivity,
        ["com.microsoft.todos"] = AppFolder.Productivity,
        ["com.dropbox.android"] = AppFolder.Productivity,
        ["com.evernote"] = AppFolder.Productivity,

        // Media
        ["com.spotify.music"] = AppFolder.Media,
        ["com.google.android.youtube"] = AppFolder.Media,
        ["com.netflix.mediaclient"] = AppFolder.Media,
        ["com.google.android.apps.youtube.music"] = AppFolder.Media,
        ["com.audible.application"] = AppFolder.Media,
        ["com.amazon.avod.thirdpartyclient"] = AppFolder.Media,
        ["com.hulu.plus"] = AppFolder.Media,
        ["com.disney.disneyplus"] = AppFolder.Media,
        ["com.google.android.apps.photos"] = AppFolder.Media,
        ["com.pandora.android"] = AppFolder.Media,

        // Smart Home
        ["com.google.android.apps.chromecast.app"] = AppFolder.SmartHome,
        ["com.nest.android"] = AppFolder.SmartHome,
        ["com.ringapp"] = AppFolder.SmartHome,
        ["com.tuya.smart"] = AppFolder.SmartHome,
        ["com.philips.lighting.hue2"] = AppFolder.SmartHome,
        ["com.ecobee.athenamobile"] = AppFolder.SmartHome,
        ["com.wyze.hodor"] = AppFolder.SmartHome,
        ["com.smartthings.android"] = AppFolder.SmartHome,
    };

    /// <summary>Parses a persisted folder name back to its enum, or null if unknown.</summary>
    public static AppFolder? FromName(string name) =>
        Enum.TryParse<AppFolder>(name, out var folder) ? folder : null;
}
