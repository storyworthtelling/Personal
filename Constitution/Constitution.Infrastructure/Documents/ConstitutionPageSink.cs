using Constitution.Domain.Documents;
using SkiaSharp;
using Storyworthtelling.Paper;

namespace Constitution.Infrastructure.Documents;

/// <summary>
/// Begins every Constitution page at the tablet's own size and at no other. A document that took its
/// size from a caller could be asked for the wrong one; this takes it from <see cref="ConstitutionPage"/>
/// and nowhere else.
/// </summary>
public static class ConstitutionPageSink
{
    public static SKCanvas Begin(VectorPdfSink sink) =>
        sink.BeginPage(ConstitutionPage.Width, ConstitutionPage.Height);
}
