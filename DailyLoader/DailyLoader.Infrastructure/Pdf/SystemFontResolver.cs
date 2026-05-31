using PdfSharp.Fonts;

namespace DailyLoader.Infrastructure.Pdf;

// PDFsharp 6 has no embedded fonts and no GDI fallback on Linux, so a font resolver must be
// registered before any XFont is created. The documents only use a sans-serif face, so this
// maps every request to the first available system sans-serif (DejaVu / Liberation / FreeSans
// on Linux, Arial on Windows) in regular/bold/italic/bold-italic variants.
public sealed class SystemFontResolver : IFontResolver
{
    private static readonly object Gate = new();
    private static bool registered;

    private static readonly Dictionary<string, byte[]> FontCache = new();

    // Face key -> candidate file paths, tried in order.
    private static readonly Dictionary<string, string[]> Faces = new()
    {
        ["sans"] =
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
            "/usr/share/fonts/truetype/freefont/FreeSans.ttf",
            "C:\\Windows\\Fonts\\arial.ttf"
        ],
        ["sans-bold"] =
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
            "/usr/share/fonts/truetype/freefont/FreeSansBold.ttf",
            "C:\\Windows\\Fonts\\arialbd.ttf"
        ],
        ["sans-italic"] =
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Oblique.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Italic.ttf",
            "/usr/share/fonts/truetype/freefont/FreeSansOblique.ttf",
            "C:\\Windows\\Fonts\\ariali.ttf"
        ],
        ["sans-bolditalic"] =
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-BoldOblique.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-BoldItalic.ttf",
            "/usr/share/fonts/truetype/freefont/FreeSansBoldOblique.ttf",
            "C:\\Windows\\Fonts\\arialbi.ttf"
        ]
    };

    // Registers the resolver exactly once; safe to call from every composer.
    public static void Register()
    {
        if (registered)
            return;

        lock (Gate)
        {
            if (registered)
                return;
            GlobalFontSettings.FontResolver = new SystemFontResolver();
            registered = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var face = (isBold, isItalic) switch
        {
            (true, true) => "sans-bolditalic",
            (true, false) => "sans-bold",
            (false, true) => "sans-italic",
            _ => "sans"
        };
        return new FontResolverInfo(face);
    }

    public byte[] GetFont(string faceName)
    {
        lock (Gate)
        {
            if (FontCache.TryGetValue(faceName, out var cached))
                return cached;

            var bytes = LoadFace(faceName);
            FontCache[faceName] = bytes;
            return bytes;
        }
    }

    private static byte[] LoadFace(string faceName)
    {
        var candidates = Faces.TryGetValue(faceName, out var paths) ? paths : Faces["sans"];
        foreach (var path in candidates)
            if (File.Exists(path))
                return File.ReadAllBytes(path);

        // Fall back to the regular face so a missing bold/italic variant still renders.
        if (faceName != "sans")
            return LoadFace("sans");

        throw new InvalidOperationException(
            "No system sans-serif font found. Install one of: fonts-dejavu, fonts-liberation, or fonts-freefont " +
            $"(searched: {string.Join(", ", candidates)}).");
    }
}
