using PdfSharp.Fonts;

namespace NytRemarkable.Android;

// PDFsharp has no GDI/CoreText on Android, so it cannot resolve fonts on its own.
// Android ships Roboto at /system/fonts since API 16; read the files directly.
public sealed class AndroidFontResolver : IFontResolver
{
    public static readonly AndroidFontResolver Instance = new();

    private const string SystemFontsPath = "/system/fonts";

    public string DefaultFontName => "Roboto";

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var weight = bold ? "Bold" : "Regular";
        var style  = italic ? "Italic" : string.Empty;
        var face   = $"Roboto-{weight}{style}";
        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName)
    {
        var path = Path.Combine(SystemFontsPath, $"{faceName}.ttf");
        if (File.Exists(path))
            return File.ReadAllBytes(path);

        var fallback = Path.Combine(SystemFontsPath, "Roboto-Regular.ttf");
        return File.Exists(fallback) ? File.ReadAllBytes(fallback) : null;
    }
}
