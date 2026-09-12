using SkiaSharp;

namespace Storyworthtelling.Paper;

/// <summary>
/// The mark a document signs off with (DESIGN.md §13) — this install's, not the repository's.
///
/// <para>It used to be one committed file, <c>/Branding/owner.png</c>, embedded in this library and
/// printed by whoever ran the app. On one phone that is a photograph of the owner. On a second
/// install it is a photograph of somebody else, on every sheet, for ever — which is the whole shape
/// of what "nothing is hardcoded to one person" means.</para>
///
/// <para>So the install supplies one and this prefers it. The embedded file stays as the fallback,
/// deliberately: the phone that has been delivering since before there was a setting keeps the mark
/// it has always printed, and does not lose it on a version bump.</para>
/// </summary>
public static class OwnerMark
{
    private static SKBitmap? installed;

    /// <summary>
    /// Hands this install its own mark, or clears it.
    ///
    /// <para>Two things here were written the obvious way first and both were wrong, and SkiaSharp
    /// says nothing about either until it is too late.</para>
    ///
    /// <para><b>A photo that will not decode throws.</b> <c>SKBitmap.Decode</c> does not return null
    /// on unreadable bytes — it raises <c>ArgumentNullException</c> from inside itself — so a file
    /// picked out of a folder that turned out not to be an image would have thrown out of a settings
    /// screen. Caught here: a bad photo costs a signature, never a morning.</para>
    ///
    /// <para><b>The mark it replaces is not disposed.</b> Disposing it is the tidy-looking thing to
    /// do and it is a use-after-free: a delivery composing a page holds the old bitmap, and touching
    /// a disposed <c>SKBitmap</c> does not throw — it takes the process down, with no managed
    /// exception and no stack. One small bitmap waiting for the collector is a trade worth making
    /// every time.</para>
    /// </summary>
    public static void Use(byte[]? png)
    {
        if (png is not { Length: > 0 })
        {
            installed = null;
            return;
        }

        try
        {
            installed = SKBitmap.Decode(png);
        }
        catch (Exception)
        {
            installed = null;
        }
    }

    /// <summary>
    /// What to print: this install's mark, else the embedded one, else nothing at all. A household
    /// that has never chosen a photo gets a page with no mark rather than an exception in the middle
    /// of a delivery — the morning is worth more than the signature on it.
    /// </summary>
    public static SKBitmap? Medallion => installed ?? EmbeddedBranding.OwnerMedallion;
}
