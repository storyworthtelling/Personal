using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace FaceStudio;

/// <summary>A decoded picture: its size and its pixels as RGBA bytes, row by row.</summary>
public sealed record Picture(int Width, int Height, byte[] Rgba);

/// <summary>Reads and writes the one PNG shape FaceStudio makes: 8-bit RGBA, not interlaced, filter 0, one zlib stream.</summary>
public static class PngPixels
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException($"{rgba.Length} bytes are not a {width} by {height} RGBA picture.", nameof(rgba));
        }

        var rows = new byte[height * (1 + width * 4)];
        for (var y = 0; y < height; y++)
        {
            rows[y * (1 + width * 4)] = 0;
            Array.Copy(rgba, y * width * 4, rows, y * (1 + width * 4) + 1, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(rows);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;

        using var png = new MemoryStream();
        png.Write(Signature);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>The pixels of a PNG this class wrote; any other PNG shape is refused rather than misread.</summary>
    public static Picture Read(byte[] png)
    {
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG: the signature is missing.");
        }

        var width = 0;
        var height = 0;
        using var data = new MemoryStream();
        var at = Signature.Length;
        while (at + 8 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            var body = png.AsSpan(at + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(body);
                height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                if (body[8] != 8 || body[9] != 6 || body[12] != 0)
                {
                    throw new InvalidDataException("Only 8-bit RGBA, not interlaced, is read.");
                }
            }
            else if (type == "IDAT")
            {
                data.Write(body);
            }
            at += 12 + length;
        }

        data.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(data, CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        var rows = inflated.ToArray();
        var stride = 1 + width * 4;
        if (rows.Length != height * stride)
        {
            throw new InvalidDataException($"{rows.Length} bytes of rows do not make a {width} by {height} picture.");
        }

        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            if (rows[y * stride] != 0)
            {
                throw new InvalidDataException($"Row {y} uses filter {rows[y * stride]}; only filter 0 is read.");
            }
            Array.Copy(rows, y * stride + 1, rgba, y * width * 4, width * 4);
        }

        return new Picture(width, height, rgba);
    }

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
