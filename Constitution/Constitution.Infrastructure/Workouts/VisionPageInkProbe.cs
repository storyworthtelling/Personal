using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// The look at a page before it is replaced or taken off (S3): it fetches what the tablet holds under the title and has the vision read
/// say whether any box the document asked about carries ink, with the same read the morning's read-back makes. The page is the cloud's
/// flattened PDF and the read goes to the same service, so this sends exactly what the read-back sends and nothing more.
///
/// <para><b>Only a clean, whole, trusted read is Blank.</b> Ink in any box, or pen ink seen anywhere on the page, is
/// <see cref="PageInk.HasInk"/> however the read otherwise went. A page is <see cref="PageInk.Blank"/> only when every page of the document was
/// read, none of them is marked, the read itself says the pages are unmarked, <i>and</i> pen ink is known to come through the cloud's PDF at all
/// (<c>inkComesThrough</c>, R3: until some page has shown it, "no ink" may only mean the pen is not in the file). Everything else is
/// <see cref="PageInk.Unknown"/>, and Unknown keeps the page. A page that is not on the tablet has nothing to lose and is Blank.</para>
///
/// <para>The failures the read can name (a refused request or a timeout, a missing key, a tablet file that came back empty) are Unknown with the
/// failure kept; a caller's own cancellation and anything unexpected propagate.</para>
/// </summary>
/// <param name="tablet">The tablet's cloud, as the read-back fetches from it.</param>
/// <param name="vision">The vision read.</param>
/// <param name="expected">The documents delivered and waiting to be read: it says which boxes a title's page was asked about.</param>
/// <param name="inkComesThrough">Whether pen ink has been seen come through the cloud's PDF on this install (the read-back's proof).</param>
public sealed class VisionPageInkProbe(IAnnotatedPageSource tablet, IInkVision vision, IExpectedDocuments expected, Func<bool> inkComesThrough) : IPageInkProbe
{
    private readonly IAnnotatedPageSource tablet = tablet ?? throw new ArgumentNullException(nameof(tablet));
    private readonly IInkVision vision = vision ?? throw new ArgumentNullException(nameof(vision));
    private readonly IExpectedDocuments expected = expected ?? throw new ArgumentNullException(nameof(expected));
    private readonly Func<bool> inkComesThrough = inkComesThrough ?? throw new ArgumentNullException(nameof(inkComesThrough));

    public async Task<InkProbe> ProbeAsync(string title, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        try
        {
            var page = await tablet.Download(title);
            if (page is null)
            {
                return InkProbe.Blank;
            }

            var document = expected.Outstanding().FirstOrDefault(entry => string.Equals(entry.Document.Name, title, StringComparison.Ordinal))?.Document;
            if (document is null)
            {
                // Nothing says which boxes this page was asked about, so there is nothing to put to the vision.
                return InkProbe.Unknown();
            }

            var read = await vision.Read(document, page.PdfBytes, cancellation);
            return Judge(document, read);
        }
        catch (Exception failure) when (failure is HttpRequestException or InvalidOperationException or ArgumentException
            || (failure is TaskCanceledException timedOut && timedOut.InnerException is TimeoutException && !cancellation.IsCancellationRequested))
        {
            return InkProbe.Unknown(failure);
        }
    }

    private InkProbe Judge(ExpectedDocument document, InkDocumentRead read)
    {
        if (read.PenStrokes == PenStrokes.Seen || document.Pages.Any(page => page.AskedTargets.Any(target => read.PageFor(page.Number).For(target.Id)?.Marked == true)))
        {
            return InkProbe.HasInk;
        }

        var wholeRead = document.Pages.All(page => read.PageFor(page.Number).WasRead);
        return wholeRead && read.PenStrokes == PenStrokes.None && inkComesThrough() ? InkProbe.Blank : InkProbe.Unknown();
    }
}
