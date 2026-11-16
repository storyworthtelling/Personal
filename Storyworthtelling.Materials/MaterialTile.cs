namespace Storyworthtelling.Materials;

/// <summary>
/// A square tile that repeats across a surface: one byte a pixel, row by row. What the byte means is the tile's
/// own: an ink alpha for the paper grain, how much colour a pencil keeps for the tooth.
/// </summary>
public sealed class MaterialTile(int size, byte[] values)
{
    public int Size { get; } = size;

    public byte[] Values { get; } = values;

    /// <summary>The value at a position, wrapping as the repeated tile does.</summary>
    public byte At(int x, int y) => Values[(Wrap(y) * Size) + Wrap(x)];

    /// <summary>
    /// A share-kept tile (the pencil's tooth) pressed (DESIGN.md §2, §8): each pixel loses <paramref name="lift"/> more
    /// of whatever its tooth already took, so the texture rises and nothing changes colour.
    /// </summary>
    public MaterialTile Lifted(double lift) =>
        new(Size, Values.Select(value => (byte)Math.Max(0, 255 - (int)Math.Round((255 - value) * (1 + lift)))).ToArray());

    /// <summary>An ink-alpha tile (the paper grain) pressed: each fleck deepens by <paramref name="lift"/>.</summary>
    public MaterialTile Grained(double lift) =>
        new(Size, Values.Select(value => (byte)Math.Min(255, (int)Math.Round(value * (1 + lift)))).ToArray());

    private int Wrap(int value) => ((value % Size) + Size) % Size;
}

/// <summary>The seeded noise every tile draws from: xorshift32, so a tile is identical on every device and in every document.</summary>
internal sealed class Noise(uint seed)
{
    private uint state = seed;

    public uint Next()
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    /// <summary>The next value as a fraction in [0, 1).</summary>
    public double Fraction() => Next() / 4294967296.0;
}
