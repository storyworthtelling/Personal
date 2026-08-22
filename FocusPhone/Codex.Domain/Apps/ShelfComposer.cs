namespace Codex.Domain.Apps;

/// <summary>
/// Composes the continuous bookcase's shelf list from the domain state: the mantel-clock shelf,
/// the home's featured cover-out books, then the tray — the owner's sibling apps (cover-out), the
/// category shelves (spine-out, each with its bookend, in catalog order), and the leftover loose
/// apps (spine-out, no bookend). Spine prominence comes from usage, ranked once across every spine
/// so sizing is consistent down the whole tray. Empty sibling/loose/category shelves are dropped;
/// the clock and home shelves always show. Pure — <see cref="BookshelfLayout"/> then places it.
/// </summary>
public static class ShelfComposer
{
    public static IReadOnlyList<ShelfSpec> Compose(
        HomeFeatureSet home,
        TrayModel tray,
        IReadOnlyDictionary<string, long> usageByPackage)
    {
        var spinePackages = tray.Folders.SelectMany(f => f.Apps).Concat(tray.LooseApps)
            .Select(a => a.PackageName).ToList();
        var sizes = SpineSizing.Rank(usageByPackage, spinePackages);

        var shelves = new List<ShelfSpec>
        {
            new(ShelfFace.Clock, []),
            new(ShelfFace.CoverOut, home.Packages.Select(p => new ShelfEntry(p)).ToList()),
        };

        if (tray.RepoApps.Count > 0)
        {
            shelves.Add(new ShelfSpec(ShelfFace.CoverOut,
                tray.RepoApps.Select(a => new ShelfEntry(a.PackageName)).ToList()));
        }

        foreach (var folder in tray.Folders)
        {
            shelves.Add(new ShelfSpec(
                ShelfFace.SpineOut,
                folder.Apps.Select(a => new ShelfEntry(a.PackageName, sizes[a.PackageName])).ToList(),
                BookendGlyph: folder.Category.Glyph,
                BookendName: folder.Category.Name));
        }

        if (tray.LooseApps.Count > 0)
        {
            shelves.Add(new ShelfSpec(ShelfFace.SpineOut,
                tray.LooseApps.Select(a => new ShelfEntry(a.PackageName, sizes[a.PackageName])).ToList()));
        }

        return shelves;
    }
}
