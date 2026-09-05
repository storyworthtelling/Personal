namespace Storyworthtelling.Paper;

/// <summary>
/// Assembles ordered page images into a single PDF. <paramref name="dpi"/> is the physical resolution each
/// full-page raster is declared at, so the PDF prints at the intended size rather than PdfSharp's default
/// 96 DPI. Domain-free: it draws images only, so no consumer's report/brief semantics leak in here.
/// </summary>
public interface IVolumePdfComposer
{
    byte[] Compose(IReadOnlyList<byte[]> pageImages, int dpi);
}
