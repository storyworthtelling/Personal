using System.Reflection;
using SkiaSharp;

namespace Storyworthtelling.Paper;

/// <summary>
/// The owner's medallion, for the Skia renderers — the maker's mark a document signs off with
/// (DESIGN.md §13).
///
/// <para>Embedded here beside the fonts, for the same reason: this is the library that prints, and a
/// renderer that cannot load a branding asset does not fail — it draws a blank where the mark should
/// be, and the page is already printed by the time anyone notices.</para>
///
/// <para>It is <b>optional</b>. A household that has not dropped a photo at <c>/Branding/owner.png</c>
/// gets a page with no mark rather than an exception in the middle of a delivery, because the
/// morning is worth more than the signature on it.</para>
/// </summary>
public static class EmbeddedBranding
{
    private static readonly Lazy<SKBitmap?> medallion = new(() =>
    {
        var assembly = typeof(EmbeddedBranding).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("owner.png", StringComparison.Ordinal));

        if (name is null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(name)!;
        return SKBitmap.Decode(stream);
    });

    public static SKBitmap? OwnerMedallion => medallion.Value;
}
