using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace FaceStudio;

/// <summary>
/// A small RGBA canvas for the shape masks and the picker's preview. Shapes are covered at
/// <see cref="Samples"/>² points a pixel, so their edges are smooth; paint goes over what is there.
/// </summary>
public sealed class Raster
{
    private const int Samples = 4;

    private readonly double[] red;
    private readonly double[] green;
    private readonly double[] blue;
    private readonly double[] alpha;

    public Raster(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"A raster of {width} by {height} has no pixels.");
        }

        Width = width;
        Height = height;
        red = new double[width * height];
        green = new double[width * height];
        blue = new double[width * height];
        alpha = new double[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The pixel's alpha, 0–1.</summary>
    public double AlphaAt(int x, int y) => alpha[Index(x, y)];

    /// <summary>The pixel as packed ARGB.</summary>
    public int ArgbAt(int x, int y)
    {
        var index = Index(x, y);
        return unchecked((Byte(alpha[index]) << 24) | (Byte(red[index]) << 16) | (Byte(green[index]) << 8) | Byte(blue[index]));
    }

    /// <summary>
    /// Paints every pixel inside <paramref name="covers"/> with the colour <paramref name="paint"/> gives
    /// for the pixel's centre, weighted by how much of the pixel is covered.
    /// </summary>
    public void Paint(Func<double, double, bool> covers, Func<double, double, int> paint)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var hits = 0;
                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        if (covers(x + (sx + 0.5) / Samples, y + (sy + 0.5) / Samples))
                        {
                            hits++;
                        }
                    }
                }

                if (hits > 0)
                {
                    Over(x, y, paint(x + 0.5, y + 0.5), (double)hits / (Samples * Samples));
                }
            }
        }
    }

    /// <summary>Paints every pixel inside <paramref name="covers"/> in one colour.</summary>
    public void Paint(Func<double, double, bool> covers, int argb) => Paint(covers, (x, y) => argb);

    /// <summary>The canvas as a PNG: 8-bit RGBA, not interlaced, one zlib stream.</summary>
    public byte[] Png()
    {
        var rows = new byte[Height * (1 + Width * 4)];
        var at = 0;
        for (var y = 0; y < Height; y++)
        {
            rows[at++] = 0;
            for (var x = 0; x < Width; x++)
            {
                var argb = ArgbAt(x, y);
                rows[at++] = (byte)(argb >> 16);
                rows[at++] = (byte)(argb >> 8);
                rows[at++] = (byte)argb;
                rows[at++] = (byte)(argb >>> 24);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(rows);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Height);
        header[8] = 8;
        header[9] = 6;

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private void Over(int x, int y, int argb, double coverage)
    {
        var index = Index(x, y);
        var source = (argb >>> 24) / 255.0 * coverage;
        var kept = alpha[index] * (1 - source);
        var total = source + kept;
        if (total <= 0)
        {
            return;
        }

        red[index] = (((argb >> 16) & 0xFF) / 255.0 * source + red[index] * kept) / total;
        green[index] = (((argb >> 8) & 0xFF) / 255.0 * source + green[index] * kept) / total;
        blue[index] = ((argb & 0xFF) / 255.0 * source + blue[index] * kept) / total;
        alpha[index] = total;
    }

    private int Index(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside a {Width} by {Height} raster.");
        }
        return y * Width + x;
    }

    private static int Byte(double unit) => (int)Math.Round(Math.Clamp(unit, 0, 1) * 255);

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        var tagged = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, tagged);
        data.CopyTo(tagged, 4);
        png.Write(tagged);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc.Of(tagged));
        png.Write(number);
    }

    /// <summary>The PNG chunk checksum (CRC-32, ISO 3309), written here since the BCL has none to hand.</summary>
    private static class Crc
    {
        private static readonly uint[] Table = Build();

        public static uint Of(byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var value in data)
            {
                crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] Build()
        {
            var table = new uint[256];
            for (var n = 0u; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }
            return table;
        }
    }
}
