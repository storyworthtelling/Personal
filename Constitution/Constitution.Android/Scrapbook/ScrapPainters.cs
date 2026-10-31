using Constitution.Domain.Scraps;

namespace Constitution.Scrapbook;

/// <summary>
/// The one painter for each <see cref="ScrapKind"/> (C-3.2.3). Built once and checked as it is
/// built: a kind with no painter, or two claiming one, stops the app at the first scrap drawn
/// rather than drawing the wrong paper. <c>ScrapPainterRegistryTests</c> reads this file and the
/// painters' own sources so the same mistake fails the suite first.
/// </summary>
public static class ScrapPainters
{
    private static readonly IReadOnlyDictionary<ScrapKind, IScrapPainter> ByKind = Register(
    [
        new MagazineClippingPainter(),
        new IndexCardPainter(),
        new NotepadLeafPainter(),
        new ReceiptPainter(),
        new ChartScrapPainter(),
    ]);

    /// <summary>The painter for <paramref name="kind"/>.</summary>
    public static IScrapPainter For(ScrapKind kind) =>
        ByKind.TryGetValue(kind, out var painter)
            ? painter
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "No scrap painter is registered for this kind.");

    private static Dictionary<ScrapKind, IScrapPainter> Register(IScrapPainter[] painters)
    {
        var table = new Dictionary<ScrapKind, IScrapPainter>();
        foreach (var painter in painters)
        {
            if (!table.TryAdd(painter.Kind, painter))
            {
                throw new InvalidOperationException(
                    $"{painter.GetType().Name} and {table[painter.Kind].GetType().Name} both draw {painter.Kind}; each kind has exactly one painter.");
            }
        }

        var missing = Enum.GetValues<ScrapKind>().Where(kind => !table.ContainsKey(kind)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException("No scrap painter is registered for: " + string.Join(", ", missing));
        }

        return table;
    }
}
