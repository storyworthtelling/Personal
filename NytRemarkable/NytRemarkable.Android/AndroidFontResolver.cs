using PdfSharp.Fonts;
using SkiaSharp;

namespace NytRemarkable.Android;

// PDFsharp requires a custom IFontResolver on non-Windows platforms because it
// has no access to GDI or CoreText. Without one, font lookups return null and
// the first draw call throws NullReferenceException. This resolver delegates to
// SkiaSharp (already present in MAUI) which uses Android's system fonts.
public sealed class AndroidFontResolver : IFontResolver
{
    public static readonly AndroidFontResolver Instance = new();

    public string DefaultFontName => "sans-serif";

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var faceName = $"{familyName}|{(bold ? 1 : 0)}|{(italic ? 1 : 0)}";
        return new FontResolverInfo(faceName);
    }

    public byte[]? GetFont(string faceName)
    {
        var parts = faceName.Split('|');
        var family = parts.Length > 0 ? parts[0] : "sans-serif";
        var bold   = parts.Length > 1 && parts[1] == "1";
        var italic = parts.Length > 2 && parts[2] == "1";

        var skStyle = (bold, italic) switch
        {
            (true,  true)  => SKFontStyle.BoldItalic,
            (true,  false) => SKFontStyle.Bold,
            (false, true)  => SKFontStyle.Italic,
            _              => SKFontStyle.Normal
        };

        using var typeface = SKTypeface.FromFamilyName(family, skStyle)
                          ?? SKTypeface.FromFamilyName("sans-serif", skStyle)
                          ?? SKTypeface.Default;
        using var data = typeface.Serialize();
        return data?.ToArray();
    }
}
