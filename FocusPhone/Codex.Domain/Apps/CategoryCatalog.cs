namespace Codex.Domain.Apps;

/// <summary>
/// The owner's ordered set of app-tray category shelves. Categories are data (feature #11):
/// the nine built-ins seed a fresh device and the owner can rename, reorder, remove, and add
/// shelves — every edit returns a NEW catalog (pure; the head persists it). The automatic
/// placement rules (a curated package map and Android's declared category) still map an app
/// to a built-in category id, and the owner's per-app override wins over both; an app whose
/// resolved category is not in the current catalog falls loose. Kept in the platform-agnostic
/// Domain so all of this is unit-tested without Android.
/// </summary>
public class CategoryCatalog
{
    // Built-in category ids reuse the old AppFolder enum names, so overrides/catalog entries a
    // device already stored ("Finance", …) keep resolving with no migration.
    public const string Finance = "Finance";
    public const string Travel = "Travel";
    public const string Shopping = "Shopping";
    public const string Food = "Food";
    public const string Health = "Health";
    public const string Utilities = "Utilities";
    public const string Productivity = "Productivity";
    public const string Media = "Media";
    public const string SmartHome = "SmartHome";

    // Android's ApplicationInfo.Category values (stable framework ints).
    private const int CategoryAudio = 1;
    private const int CategoryVideo = 2;
    private const int CategoryImage = 3;
    private const int CategoryMaps = 6;
    private const int CategoryProductivity = 7;
    private const int CategoryAccessibility = 8;

    private readonly List<Category> categories;

    public CategoryCatalog(IEnumerable<Category> categories) =>
        this.categories = categories.OrderBy(c => c.Order).ToList();

    /// <summary>The categories in display (shelf) order.</summary>
    public IReadOnlyList<Category> Categories => categories;

    /// <summary>A fresh catalog with the nine built-in shelves in their default order.</summary>
    public static CategoryCatalog Seed() => new(
    [
        new(Finance, "Finance", "folder_finance", 0, Builtin: true),
        new(Travel, "Travel & Transit", "folder_travel", 1, Builtin: true),
        new(Shopping, "Shopping", "folder_shopping", 2, Builtin: true),
        new(Food, "Food & Dining", "folder_food", 3, Builtin: true),
        new(Health, "Health & Fitness", "folder_health", 4, Builtin: true),
        new(Utilities, "Utilities & Tools", "folder_utilities", 5, Builtin: true),
        new(Productivity, "Productivity", "folder_productivity", 6, Builtin: true),
        new(Media, "Media", "folder_media", 7, Builtin: true),
        new(SmartHome, "Smart Home", "folder_smarthome", 8, Builtin: true),
    ]);

    public Category? ById(string id) => categories.FirstOrDefault(c => c.Id == id);

    public bool Contains(string id) => categories.Any(c => c.Id == id);

    /// <summary>Adds an owner-created shelf at the end. The id is a unique slug of the name so
    /// it is stable across reorders/renames; the glyph is generated from the name by the head.</summary>
    public CategoryCatalog WithAdded(string name, string glyph)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A shelf needs a name.", nameof(name));
        }
        var id = UniqueId(Slugify(trimmed));
        var order = categories.Count == 0 ? 0 : categories.Max(c => c.Order) + 1;
        return new CategoryCatalog(categories.Append(new Category(id, trimmed, glyph, order, Builtin: false)));
    }

    /// <summary>Renames a shelf (built-in or owner-made); the id and glyph are unchanged.</summary>
    public CategoryCatalog WithRenamed(string id, string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A shelf needs a name.", nameof(name));
        }
        return new CategoryCatalog(categories.Select(c => c.Id == id ? c with { Name = trimmed } : c));
    }

    /// <summary>Removes a shelf (built-ins are removable too); its apps fall loose. No-op if absent.</summary>
    public CategoryCatalog WithRemoved(string id) =>
        new(categories.Where(c => c.Id != id));

    /// <summary>Re-orders the shelves to the given id sequence; any ids not named keep their
    /// relative order after the named ones (so a partial reorder is safe).</summary>
    public CategoryCatalog Reordered(IReadOnlyList<string> orderedIds)
    {
        var rank = orderedIds
            .Select((id, index) => (id, index))
            .ToDictionary(t => t.id, t => t.index);
        var reordered = categories
            .OrderBy(c => rank.TryGetValue(c.Id, out var r) ? r : int.MaxValue)
            .ThenBy(c => c.Order)
            .Select((c, index) => c with { Order = index });
        return new CategoryCatalog(reordered);
    }

    // ---- Automatic placement (maps an app to a built-in category id) ----

    /// <summary>
    /// The reliable slice of Android's declared category. Only categories that map
    /// unambiguously to one shelf are honored; Social, News, Undefined and Game return null so
    /// those apps fall through to loose (or a curated/override placement).
    /// </summary>
    public static string? FromAndroidCategory(int androidCategory) => androidCategory switch
    {
        CategoryMaps => Travel,
        CategoryAudio or CategoryVideo or CategoryImage => Media,
        CategoryProductivity => Productivity,
        CategoryAccessibility => Utilities,
        _ => null,
    };

    /// <summary>
    /// Well-known packages that rarely declare a usable Android category, mapped to a built-in
    /// category id. Not exhaustive — anything missing lands loose until the owner files it.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CuratedSeed { get; } = new Dictionary<string, string>
    {
        // Finance
        ["com.google.android.apps.walletnfcrel"] = Finance,
        ["com.paypal.android.p2pmobile"] = Finance,
        ["com.venmo"] = Finance,
        ["com.chase.sig.android"] = Finance,
        ["com.infonow.bofa"] = Finance,
        ["com.wf.wellsfargomobile"] = Finance,
        ["com.konylabs.capitalone"] = Finance,
        ["com.usaa.mobile.android.usaa"] = Finance,
        ["com.discoverfinancial.mobile"] = Finance,
        ["com.citi.citimobile"] = Finance,
        ["com.americanexpress.android.acctsvcs.us"] = Finance,
        ["com.robinhood.android"] = Finance,
        ["com.coinbase.android"] = Finance,
        ["com.fidelity.android"] = Finance,
        ["com.schwab.mobile"] = Finance,
        ["com.intuit.mint"] = Finance,

        // Travel & Transit
        ["com.ubercab"] = Travel,
        ["com.lyft.android"] = Travel,
        ["com.waze"] = Travel,
        ["com.google.android.apps.maps"] = Travel,
        ["com.tripit"] = Travel,
        ["com.united.mobile.android"] = Travel,
        ["com.aa.android"] = Travel,
        ["com.delta.mobile.android"] = Travel,
        ["com.southwestairlines.mobile"] = Travel,
        ["com.airbnb.android"] = Travel,
        ["com.booking"] = Travel,
        ["com.tripadvisor.tripadvisor"] = Travel,
        ["com.google.android.apps.transit"] = Travel,

        // Shopping
        ["com.amazon.mShop.android.shopping"] = Shopping,
        ["com.ebay.mobile"] = Shopping,
        ["com.walmart.android"] = Shopping,
        ["com.target.ui"] = Shopping,
        ["com.etsy.android"] = Shopping,
        ["com.contextlogic.wish"] = Shopping,
        ["com.bestbuy.android"] = Shopping,
        ["com.thehomedepot"] = Shopping,

        // Food & Dining
        ["com.ubercab.eats"] = Food,
        ["com.dd.doordash"] = Food,
        ["com.grubhub.android"] = Food,
        ["com.yelp.android"] = Food,
        ["com.starbucks.mobilecard"] = Food,
        ["com.mcdonalds.app"] = Food,
        ["com.chipotle.ordering"] = Food,
        ["com.instacart.client"] = Food,
        ["com.kroger.mobile"] = Food,
        ["com.safeway.client.android.safeway"] = Food,

        // Health & Fitness
        ["com.fitbit.FitbitMobile"] = Health,
        ["com.google.android.apps.fitness"] = Health,
        ["com.myfitnesspal.android"] = Health,
        ["com.strava"] = Health,
        ["com.headspace.android"] = Health,
        ["com.calm.android"] = Health,
        ["com.walgreens.android.application"] = Health,
        ["com.cvs.launchers.cvs"] = Health,
        ["epic.mychart.android"] = Health,

        // Utilities & Tools
        ["com.google.android.calculator"] = Utilities,
        ["com.google.android.deskclock"] = Utilities,
        ["com.google.android.apps.nbu.files"] = Utilities,
        ["com.google.android.apps.weather"] = Utilities,
        ["com.google.android.apps.authenticator2"] = Utilities,

        // Productivity
        ["com.google.android.calendar"] = Productivity,
        ["com.google.android.keep"] = Productivity,
        ["com.microsoft.office.outlook"] = Productivity,
        ["com.google.android.apps.docs.editors.docs"] = Productivity,
        ["com.google.android.apps.docs.editors.sheets"] = Productivity,
        ["com.google.android.apps.docs.editors.slides"] = Productivity,
        ["com.todoist"] = Productivity,
        ["com.notion.id"] = Productivity,
        ["com.microsoft.todos"] = Productivity,
        ["com.dropbox.android"] = Productivity,
        ["com.evernote"] = Productivity,

        // Media
        ["com.spotify.music"] = Media,
        ["com.google.android.youtube"] = Media,
        ["com.netflix.mediaclient"] = Media,
        ["com.google.android.apps.youtube.music"] = Media,
        ["com.audible.application"] = Media,
        ["com.amazon.avod.thirdpartyclient"] = Media,
        ["com.hulu.plus"] = Media,
        ["com.disney.disneyplus"] = Media,
        ["com.google.android.apps.photos"] = Media,
        ["com.pandora.android"] = Media,

        // Smart Home
        ["com.google.android.apps.chromecast.app"] = SmartHome,
        ["com.nest.android"] = SmartHome,
        ["com.ringapp"] = SmartHome,
        ["com.tuya.smart"] = SmartHome,
        ["com.philips.lighting.hue2"] = SmartHome,
        ["com.ecobee.athenamobile"] = SmartHome,
        ["com.wyze.hodor"] = SmartHome,
        ["com.smartthings.android"] = SmartHome,
    };

    private static string Slugify(string name)
    {
        var chars = name.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }
        return slug.Length == 0 ? "shelf" : slug;
    }

    private string UniqueId(string baseId)
    {
        if (!Contains(baseId))
        {
            return baseId;
        }
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseId}-{n}";
            if (!Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
