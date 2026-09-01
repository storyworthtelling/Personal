namespace Codex.Domain.Apps;

/// <summary>
/// One app-tray category shelf. Categories are DATA now (not a fixed enum): the nine
/// built-ins are seed data the owner can rename, reorder, and remove, and the owner can
/// create new ones (feature #11). <see cref="Id"/> is the stable key persisted in overrides
/// and in the catalog store — the built-in ids are the old enum names, so a device that
/// stored "Finance" keeps resolving after the enum→data change with no migration.
/// </summary>
/// <param name="Id">Stable key (built-ins reuse the old enum names, e.g. "Finance").</param>
/// <param name="Name">Display name on the bookend (editable).</param>
/// <param name="Glyph">The engraved bookend glyph id: a built-in "folder_*" drawable, or an
/// owner-created glyph id generated from the name (AI vector; monogram fallback offline).</param>
/// <param name="Order">Position among the category shelves (owner-reorderable).</param>
/// <param name="Builtin">True for one of the nine seeds; false for an owner-created shelf.</param>
public record Category(string Id, string Name, string Glyph, int Order, bool Builtin);
