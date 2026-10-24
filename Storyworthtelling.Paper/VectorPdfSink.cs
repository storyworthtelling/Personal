using SkiaSharp;

namespace Storyworthtelling.Paper;

/// <summary>
/// A PDF built from real vector pages: the same Skia draw calls the composers already make, sent to
/// a PDF canvas instead of a bitmap.
///
/// <para>Every document in the suite has been a 150-DPI PNG per page wrapped by PdfSharp — a
/// <b>picture</b> of a page. Nothing in it can be selected or searched, it is roughly thirty times
/// the size, and on an e-ink panel it is resampled twice: once when the page is rasterised at 150
/// DPI and again to the panel's own grid, which is what makes small type look soft. Sending the same
/// calls here keeps text as text, embeds the fonts, and leaves the geometry resolution-independent,
/// so the tablet renders it at whatever the panel actually is.</para>
///
/// <para>It lives in the shared library rather than in one app because a second implementation is
/// how the two copies of the design system happened; every head that draws a document draws it
/// through this.</para>
///
/// <para>Skia's PDF backend writes as it goes, so the bytes are only complete once
/// <see cref="Close"/> has run. That is why this is a sink rather than a composer taking a list of
/// pages: holding every page in memory to hand over at the end is exactly the cost being removed.</para>
/// </summary>
public sealed class VectorPdfSink : IDisposable
{
    /// <summary>Skia's value for "write pictures losslessly", and the default.</summary>
    public const int Lossless = 101;

    private readonly MemoryStream stream = new();
    private readonly SKDocument document;
    private int pagesBegun;
    private bool closed;

    /// <param name="imageQuality">
    /// <see cref="Lossless"/> (the default), or 1–100 to have every <b>opaque</b> picture written as a
    /// JPEG at that quality. Skia writes pictures losslessly otherwise, which is right for a document
    /// whose pictures are line work and wrong for one whose pictures are illustrations: a report with 67
    /// of them came out at 173 MB, over the reMarkable's documented 100 MB, and large enough to run a
    /// phone out of memory just holding the bytes. Text and vector drawing are unaffected either way.
    /// </param>
    public VectorPdfSink(int imageQuality = Lossless)
    {
        document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { EncodingQuality = imageQuality })
            ?? throw new InvalidOperationException(
                "Skia would not create a PDF document. The native library is probably missing — a "
                + "head needs SkiaSharp.NativeAssets for its platform.");
    }

    /// <summary>Pages written so far, including one in progress.</summary>
    public int PageCount => pagesBegun;

    /// <summary>
    /// Starts a page and returns its canvas, in <b>points</b>. No scaling: a PDF's user space is
    /// already 1/72 inch, which is the unit the composers have always measured in — the raster path
    /// only scaled because a bitmap has pixels.
    /// </summary>
    public SKCanvas BeginPage(float widthPoints, float heightPoints)
    {
        ThrowIfClosed();
        pagesBegun++;
        return document.BeginPage(widthPoints, heightPoints);
    }

    public void EndPage()
    {
        ThrowIfClosed();
        document.EndPage();
    }

    /// <summary>
    /// Finishes the document and returns its bytes. Once only: Skia's writer appends the trailer on
    /// close, so a second call would produce a truncated or empty file rather than the same one
    /// again — a silent corruption is a worse answer than an exception.
    /// </summary>
    public byte[] Close()
    {
        ThrowIfClosed();

        if (pagesBegun == 0)
        {
            throw new InvalidOperationException(
                "A document with no pages was closed. A PDF with no pages opens as a damaged file "
                + "in most readers, which reads as a delivery failure rather than an empty brief.");
        }

        closed = true;
        document.Close();
        return stream.ToArray();
    }

    public void Dispose()
    {
        document.Dispose();
        stream.Dispose();
    }

    private void ThrowIfClosed()
    {
        if (closed)
        {
            throw new InvalidOperationException("This document has already been closed.");
        }
    }
}
