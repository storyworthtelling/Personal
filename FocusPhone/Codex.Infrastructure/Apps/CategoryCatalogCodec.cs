using System.Text.Json;
using Codex.Domain.Apps;

namespace Codex.Infrastructure.Apps;

/// <summary>
/// JSON encode/decode for the owner's app-tray category catalog, so the Android
/// SharedPreferences store stays a thin adapter and the (de)serialization is unit-tested
/// without a device. The catalog is stored as a flat array of shelves (id, name, glyph,
/// order, builtin).
/// <para>
/// An <em>absent</em> payload (a fresh device, or the store never written) decodes to
/// <see cref="CategoryCatalog.Seed"/> — the nine built-ins. A <em>garbled</em> payload also
/// falls back to the seeds rather than throwing. But a <em>valid, empty</em> catalog is
/// preserved as-is: the owner removing every shelf is an intentional state, not a reason to
/// re-seed.
/// </para>
/// </summary>
public static class CategoryCatalogCodec
{
    public static string Encode(CategoryCatalog catalog) =>
        JsonSerializer.Serialize(catalog.Categories);

    public static CategoryCatalog Decode(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return CategoryCatalog.Seed();
        }

        try
        {
            var categories = JsonSerializer.Deserialize<List<Category>>(json);
            return categories is null ? CategoryCatalog.Seed() : new CategoryCatalog(categories);
        }
        catch (JsonException)
        {
            return CategoryCatalog.Seed();
        }
    }
}
