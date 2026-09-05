using Android.Content;
using Codex.Domain.Apps;
using Codex.Infrastructure.Apps;

namespace Flyleaf;

/// <summary>
/// Persists the owner's app-tray category catalog (feature #11): the nine built-in shelves
/// seed a fresh device, and the owner's create / rename / reorder / remove edits are stored
/// as the whole catalog. A thin SharedPreferences adapter over <see cref="CategoryCatalogCodec"/>
/// — the (de)serialization and the seed/empty fallbacks are unit-tested there without a device.
/// An absent store loads the seeds; a valid empty catalog (every shelf removed) is preserved.
/// The preferences file name ("categories") is a storage key, kept stable across renames.
/// </summary>
public class CategoryStore
{
    private const string PreferencesName = "categories";
    private const string CatalogKey = "catalog"; // JSON array of shelves

    private readonly ISharedPreferences preferences;

    public CategoryStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    /// <summary>The stored catalog, or the nine built-in shelves when nothing has been saved.</summary>
    public CategoryCatalog Load() =>
        CategoryCatalogCodec.Decode(preferences.GetString(CatalogKey, null));

    /// <summary>Persists the owner's edited catalog.</summary>
    public void Save(CategoryCatalog catalog) =>
        preferences.Edit()!.PutString(CatalogKey, CategoryCatalogCodec.Encode(catalog))!.Apply();
}
