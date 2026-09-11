using System.Reflection;
using SkiaSharp;

namespace Storyworthtelling.Paper;

/// <summary>
/// The Paper &amp; Ink font families for the Skia renderers, loaded once from
/// embedded copies of the shared repo fonts. Embedded resources behave
/// identically on the desktop test TFM and on device.
/// </summary>
public static class EmbeddedTypefaces
{
    private static readonly Lazy<SKTypeface> serifRegular = Load("SourceSerif4-Regular.ttf");
    private static readonly Lazy<SKTypeface> serifSemibold = Load("SourceSerif4-Semibold.ttf");
    private static readonly Lazy<SKTypeface> serifBlack = Load("SourceSerif4-Black.ttf");
    private static readonly Lazy<SKTypeface> serifItalic = Load("SourceSerif4-Italic.ttf");
    private static readonly Lazy<SKTypeface> sansRegular = Load("SourceSans3-Regular.ttf");
    private static readonly Lazy<SKTypeface> sansSemibold = Load("SourceSans3-Semibold.ttf");
    private static readonly Lazy<SKTypeface> script = Load("PinyonScript-Regular.ttf");
    private static readonly Lazy<SKTypeface> mono = Load("CascadiaCode-Regular.ttf");

    public static SKTypeface SerifRegular => serifRegular.Value;
    public static SKTypeface SerifSemibold => serifSemibold.Value;

    /// <summary>Source Serif 4 Black — the reworked Display weight for chapter-opening headers (P4).</summary>
    public static SKTypeface SerifBlack => serifBlack.Value;
    public static SKTypeface SerifItalic => serifItalic.Value;
    public static SKTypeface SansRegular => sansRegular.Value;
    public static SKTypeface SansSemibold => sansSemibold.Value;

    /// <summary>
    /// Pinyon Script — the calligraphy hand DESIGN.md §4 reserves for the owner's voice, and
    /// nothing else. It shipped inside every app and was not embedded here, so the one library that
    /// prints a page could not draw it: Skia substitutes a face and says nothing, and the narrator's
    /// sentence comes out in the same hand as the rest of the sheet, which is exactly the
    /// distinction §4 exists to make.
    /// </summary>
    public static SKTypeface Script => script.Value;

    /// <summary>Cascadia Code — the Mono role, which the printed type ladder has a size for.</summary>
    public static SKTypeface Mono => mono.Value;

    private static Lazy<SKTypeface> Load(string fileName) => new(() =>
    {
        var assembly = typeof(EmbeddedTypefaces).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Font resource not embedded: {fileName}");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException($"SkiaSharp could not load font: {fileName}");
    });
}
