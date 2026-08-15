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
    private static readonly Lazy<SKTypeface> serifItalic = Load("SourceSerif4-Italic.ttf");
    private static readonly Lazy<SKTypeface> sansRegular = Load("SourceSans3-Regular.ttf");
    private static readonly Lazy<SKTypeface> sansSemibold = Load("SourceSans3-Semibold.ttf");

    public static SKTypeface SerifRegular => serifRegular.Value;
    public static SKTypeface SerifSemibold => serifSemibold.Value;
    public static SKTypeface SerifItalic => serifItalic.Value;
    public static SKTypeface SansRegular => sansRegular.Value;
    public static SKTypeface SansSemibold => sansSemibold.Value;

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
