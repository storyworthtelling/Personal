using Constitution.Domain.Scraps;

namespace Constitution.Domain.Ground;

/// <summary>A point in dp, origin at the well's top-left.</summary>
public readonly record struct CrinklePoint(double X, double Y);

/// <summary>One wandering crease. Intensity is unitless 0..1 opacity; the painter maps it to the registered crease ink.</summary>
public sealed record Crease(IReadOnlyList<CrinklePoint> Points, double Intensity);

/// <summary>One short crinkle tick.</summary>
public readonly record struct CrinkleTick(CrinklePoint A, CrinklePoint B, double Intensity);

/// <summary>The single tear line, as a fraction of the well's height.</summary>
public readonly record struct Perforation(double YFraction);

/// <summary>
/// The crinkled exam-table ground (D43, book p.11) as pure geometry: procedural creases and
/// crinkle ticks plus exactly one perforated tear line. Intensities are unitless; the painter
/// maps them to the registered crease/perforation ink, alpha and widths in MaterialTokens, so no
/// colour lives here. The generator owns its xorshift so a seed means the same ground forever,
/// which System.Random does not promise across runtimes.
/// </summary>
public sealed record CrinkleField(
    IReadOnlyList<Crease> Creases,
    IReadOnlyList<CrinkleTick> Ticks,
    Perforation Perforation)
{
    /// <summary>
    /// The binder gutter's width: the scrap page's own figure, which a test holds equal to the suite's
    /// <c>BinderTabs.WidthDp</c>. (A binder tab's height is a different quantity that merely shares the number.)
    /// </summary>
    public const int GutterWidthDp = ScrapMetrics.BinderGutterDp;

    public const double MinIntensity = 0.04;
    public const double MaxIntensity = 0.09;

    /// <summary>The mockup's tile: 7 creases and about 100 ticks over 260 x 260.</summary>
    private const int TileSideDp = 260;
    private const int CreasesPerTile = 7;
    private const int TicksPerTile = 100;
    private const int CreaseSteps = 6;
    private const double TickLengthDp = 9;

    /// <summary>Roughly where the roll tears: 640 of an ~844 dp page, varied a little by seed.</summary>
    private const double PerforationBase = 0.76;
    private const double PerforationSpread = 0.04;

    /// <summary>A repeating tile, at the mockup's density scaled to the tile's area.</summary>
    public static CrinkleField Generate(int seed, int tileWidthDp, int tileHeightDp) =>
        Build(seed, tileWidthDp, tileHeightDp);

    /// <summary>The well only: every point has 0 &lt;= x &lt;= wellWidthDp. The caller passes the well, not the page.</summary>
    public static CrinkleField ForWell(int seed, int wellWidthDp, int wellHeightDp) =>
        Build(seed, wellWidthDp, wellHeightDp);

    /// <summary>The well of a page of this width, which is the page less the binder gutter.</summary>
    public static CrinkleField ForPage(int seed, int pageWidthDp, int pageHeightDp) =>
        Build(seed, Math.Max(pageWidthDp - GutterWidthDp, 0), pageHeightDp);

    private static CrinkleField Build(int seed, int width, int height)
    {
        if (width < 0 || height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "A ground cannot have a negative size.");
        }

        var rng = new Xorshift(seed);
        var areaRatio = (double)width * height / (TileSideDp * TileSideDp);
        var creaseCount = Math.Max((int)Math.Round(CreasesPerTile * areaRatio), CreasesPerTile);
        var tickCount = (int)Math.Round(TicksPerTile * areaRatio);

        var creases = new List<Crease>(creaseCount);
        for (var i = 0; i < creaseCount; i++)
        {
            creases.Add(NewCrease(rng, width, height));
        }

        var ticks = new List<CrinkleTick>(tickCount);
        for (var i = 0; i < tickCount; i++)
        {
            ticks.Add(NewTick(rng, width, height));
        }

        var perforation = new Perforation(PerforationBase + ((rng.Next() - 0.5) * 2 * PerforationSpread));
        return new CrinkleField(creases, ticks, perforation);
    }

    private static Crease NewCrease(Xorshift rng, int width, int height)
    {
        var points = new List<CrinklePoint>(CreaseSteps + 1);
        var x = rng.Next() * width;
        var y = rng.Next() * height;
        var heading = rng.Next() * Math.PI * 2;
        var step = Math.Max(Math.Min(width, height) / 4.0, 1);
        points.Add(new CrinklePoint(x, y));
        for (var i = 0; i < CreaseSteps; i++)
        {
            heading += (rng.Next() - 0.5) * 1.4;
            x = Math.Clamp(x + (Math.Cos(heading) * step), 0, width);
            y = Math.Clamp(y + (Math.Sin(heading) * step), 0, height);
            points.Add(new CrinklePoint(x, y));
        }

        return new Crease(points, Intensity(rng));
    }

    private static CrinkleTick NewTick(Xorshift rng, int width, int height)
    {
        var x = rng.Next() * width;
        var y = rng.Next() * height;
        var angle = rng.Next() * Math.PI * 2;
        var length = TickLengthDp * (0.4 + (rng.Next() * 0.6));
        var end = new CrinklePoint(
            Math.Clamp(x + (Math.Cos(angle) * length), 0, width),
            Math.Clamp(y + (Math.Sin(angle) * length), 0, height));
        return new CrinkleTick(new CrinklePoint(x, y), end, Intensity(rng));
    }

    private static double Intensity(Xorshift rng) =>
        MinIntensity + (rng.Next() * (MaxIntensity - MinIntensity));

    /// <summary>xorshift32; yields doubles in [0, 1).</summary>
    private sealed class Xorshift
    {
        private uint state;

        public Xorshift(int seed)
        {
            // Mix the seed so small neighbouring seeds diverge, and never start at zero.
            state = (uint)seed * 2654435761u + 0x9E3779B9u;
            if (state == 0)
            {
                state = 1;
            }

            for (var i = 0; i < 4; i++)
            {
                Step();
            }
        }

        public double Next() => Step() / 4294967296.0;

        private uint Step()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
    }
}
