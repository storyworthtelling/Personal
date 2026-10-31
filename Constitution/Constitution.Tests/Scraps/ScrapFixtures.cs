using Constitution.Domain.Scraps;

namespace Constitution.Tests.Scraps;

/// <summary>Invented scraps for the layout sweeps. Ids and heights are made up.</summary>
internal static class ScrapFixtures
{
    /// <summary>The phone widths the acceptance names, 360 dp to 430 dp.</summary>
    public static IEnumerable<int> PhoneWidths() => Enumerable.Range(360, 71);

    public static ScrapKind[] AllKinds { get; } = Enum.GetValues<ScrapKind>();

    /// <summary>
    /// <paramref name="count"/> scraps cycling through every kind, starting at a different kind
    /// per <paramref name="start"/>, with heights that vary from squat to tall.
    /// </summary>
    public static List<Scrap> Mixed(int count, int start = 0)
    {
        var scraps = new List<Scrap>();
        for (var i = 0; i < count; i++)
        {
            var kind = AllKinds[(i + start) % AllKinds.Length];
            var height = 36 + (((i * 53) + (start * 17)) % 240);
            scraps.Add(new Scrap(kind, "scrap-" + start + "-" + i, height));
        }

        return scraps;
    }

    public static List<Scrap> Random(Random random)
    {
        var count = random.Next(1, 13);
        var scraps = new List<Scrap>();
        for (var i = 0; i < count; i++)
        {
            var kind = AllKinds[random.Next(AllKinds.Length)];
            scraps.Add(new Scrap(kind, "r" + random.Next(100000) + "-" + i, 16 + (random.NextDouble() * 420)));
        }

        return scraps;
    }

    public static double WellWidthFor(int phoneWidth) => ScrapLayout.WellWidthDp(phoneWidth);
}
