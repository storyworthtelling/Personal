namespace Storyworthtelling.Materials;

/// <summary>
/// The paper grain (DESIGN.md §2): seeded ink flecks over the flat paper colour. The same tile behind every
/// native screen (IconStudio's paper_grain.png), every MAUI ground and every document page, so they are one paper.
/// Each value is the fleck's ink alpha, or 0.
/// </summary>
public static class PaperGrainTile
{
    public static MaterialTile Build()
    {
        var noise = new Noise(Grain.Seed);
        var values = new byte[Grain.TileSize * Grain.TileSize];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = noise.Next() % Grain.Density == 0 ? (byte)Grain.Alpha : (byte)0;
        }
        return new MaterialTile(Grain.TileSize, values);
    }
}

/// <summary>
/// Colored pencil's tooth (DESIGN.md §2): how much of the pencil's colour each pixel keeps as it skips over the
/// paper's fibre. Half the pixels lose some colour and a tenth drop out entirely; the flakes run diagonally in
/// clusters rather than scattering one by one, which is what makes it read as pencil and not as noise.
/// Each value is the share kept, of 255.
/// </summary>
public static class PencilTooth
{
    public const int Size = 64;

    private const uint FibreSeed = 2003;
    private const uint DropSeed = 811;

    public static MaterialTile Build()
    {
        var fibre = Field(FibreSeed);
        var drop = Field(DropSeed);
        double At(double[] field, int x, int y) => field[(((y % Size) + Size) % Size * Size) + (((x % Size) + Size) % Size)];

        var values = new byte[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                // Each pixel shares its fibre with its diagonal neighbours, so a flake runs on into the next.
                var tooth = (0.5 * At(fibre, x, y)) + (0.3 * At(fibre, x + 1, y + 1)) + (0.2 * At(fibre, x - 1, y - 1));
                double kept;
                if (At(drop, x, y) < MaterialStrength.PencilDropsOut)
                {
                    kept = 0;
                }
                else if (tooth < MaterialStrength.PencilFlakes)
                {
                    var depth = tooth / MaterialStrength.PencilFlakes;
                    kept = MaterialStrength.PencilKeepsAtLeast + ((MaterialStrength.PencilKeepsAtMost - MaterialStrength.PencilKeepsAtLeast) * depth);
                }
                else
                {
                    kept = 1;
                }
                values[(y * Size) + x] = (byte)Math.Round(kept * 255);
            }
        }
        return new MaterialTile(Size, values);
    }

    private static double[] Field(uint seed)
    {
        var noise = new Noise(seed);
        var field = new double[Size * Size];
        for (var i = 0; i < field.Length; i++)
        {
            field[i] = noise.Fraction();
        }
        return field;
    }
}

/// <summary>
/// The streaks of the felt-tip media (DESIGN.md §2). A streak runs the length of the stroke, so each is one value a
/// row across the stroke's width, repeated along it: the marker's are shares of its colour kept, the
/// highlighter's are its alpha. Painters lay them along the stroke, never across it.
/// </summary>
public static class Streaks
{
    public const int Rows = 64;

    private const uint MarkerSeed = 11;
    private const uint HighlighterSeed = 5;

    /// <summary>The marker's streaks: each row keeps between four fifths and all of the colour.</summary>
    public static byte[] Marker()
    {
        var walk = Walk(MarkerSeed);
        return walk.Select(value => (byte)Math.Round((1 - (MaterialStrength.MarkerStreak * value)) * 255)).ToArray();
    }

    /// <summary>How much of the marker's colour is left at a point along the stroke (0 its start, 1 its end): it thins in the last tenth.</summary>
    public static double MarkerEnd(double along) =>
        along <= 0.9 ? 1.0 : 1 - (MaterialStrength.MarkerEndThins * Math.Min(1, (along - 0.9) / 0.1));

    /// <summary>The highlighter's streaks, as alphas: between the faintest and strongest streak, averaging the wash.</summary>
    public static byte[] Highlighter()
    {
        var least = MaterialStrength.HighlighterLeast * 255;
        var most = MaterialStrength.HighlighterMost * 255;
        var walk = Walk(HighlighterSeed);
        var mean = walk.Average();
        return walk
            .Select(value => Math.Clamp(MaterialStrength.WashAlpha + ((value - mean) * (most - least)), least, most))
            .Select(value => (byte)Math.Round(value))
            .ToArray();
    }

    /// <summary>
    /// Where the nib started and lifted, the band pools: the alpha the band reaches at <paramref name="x"/> of a
    /// band <paramref name="width"/> long, falling to nothing a short way in. Zero means no pooling here; a painter
    /// takes the stronger of this and the streak.
    /// </summary>
    public static double HighlighterPool(double x, double width)
    {
        var reach = Math.Max(8, width * 0.05);
        var fromEnd = Math.Min(x, width - 1 - x);
        return fromEnd >= reach ? 0 : MaterialStrength.HighlighterPool * (1 - (fromEnd / reach));
    }

    // A mean-reverting walk in [0, 1]: neighbouring rows are close, as fibres in one felt tip are.
    private static double[] Walk(uint seed)
    {
        var noise = new Noise(seed);
        var rows = new double[Rows];
        var drift = 0.0;
        for (var i = 0; i < Rows; i++)
        {
            drift = (drift * 0.6) + ((noise.Fraction() - 0.5) * 0.5);
            rows[i] = Math.Clamp(0.5 + drift + ((noise.Fraction() - 0.5) * 0.4), 0, 1);
        }
        return rows;
    }
}
