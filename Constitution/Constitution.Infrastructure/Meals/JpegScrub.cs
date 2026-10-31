namespace Constitution.Infrastructure.Meals;

/// <summary>
/// Takes the metadata out of a JPEG before it goes anywhere: the EXIF block (where a phone writes the place a photograph was taken,
/// the time and the device), the other application blocks and the comments. The picture itself, its tables and its scan are copied
/// byte for byte. Anything that is not a whole JPEG up to its scan is refused (null) rather than passed on unchecked.
/// </summary>
public static class JpegScrub
{
    private const byte Marker = 0xFF;
    private const byte StartOfImage = 0xD8;
    private const byte StartOfScan = 0xDA;
    private const byte FirstApplication = 0xE0;
    private const byte LastApplication = 0xEF;
    private const byte Comment = 0xFE;

    /// <summary>The same picture without EXIF, application blocks (except JFIF's) and comments, or null when the bytes are not a JPEG.</summary>
    public static byte[]? WithoutMetadata(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        if (jpeg.Length < 4 || jpeg[0] != Marker || jpeg[1] != StartOfImage)
        {
            return null;
        }

        var output = new List<byte>(jpeg.Length) { Marker, StartOfImage };
        var at = 2;
        while (at < jpeg.Length)
        {
            if (jpeg[at] != Marker)
            {
                return null;
            }

            while (at < jpeg.Length && jpeg[at] == Marker)
            {
                at++;
            }

            if (at >= jpeg.Length)
            {
                return null;
            }

            var kind = jpeg[at++];
            if (kind == StartOfScan)
            {
                // The scan and everything after it is the picture's own data.
                output.Add(Marker);
                output.Add(kind);
                output.AddRange(jpeg.AsSpan(at).ToArray());
                return at < jpeg.Length ? output.ToArray() : null;
            }

            if (at + 2 > jpeg.Length)
            {
                return null;
            }

            var length = (jpeg[at] << 8) | jpeg[at + 1];
            if (length < 2 || at + length > jpeg.Length)
            {
                return null;
            }

            var isMetadata = kind == Comment || (kind is > FirstApplication and <= LastApplication);
            if (!isMetadata)
            {
                output.Add(Marker);
                output.Add(kind);
                output.AddRange(jpeg.AsSpan(at, length).ToArray());
            }

            at += length;
        }

        return null;
    }
}
