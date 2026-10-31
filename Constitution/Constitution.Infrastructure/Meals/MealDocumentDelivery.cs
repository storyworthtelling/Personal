using Constitution.Domain.Delivery;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Delivery;
using Constitution.Infrastructure.Workouts;
using Storyworthtelling.Remarkable;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// Puts a meal sheet or a recipe on the tablet, or replaces one, over <see cref="DeliveryService"/> so a refused token is still the same Problem
/// notice and the same retry. The meals counterpart of <see cref="WorkoutDocumentDelivery"/>, with the same two promises.
///
/// <para><b>It looks before it takes a page off.</b> The read-back reads a blank sheet as "everything approved" and a blank rating page as "unchanged", so trashing
/// a page the owner has begun writing on, or setting a blank copy down over it, loses what she wrote. Before any page is trashed it is asked about
/// (<see cref="IPageInkProbe"/>): only <see cref="PageInk.Blank"/> lets it go, and ink on it, or no way to say, leaves it exactly where it is.</para>
///
/// <para><b>It never makes a second document.</b> The cloud does not replace by title, and the read-back finds a page by title. A first send, which has
/// no record of what is on the tablet, looks the title up before it uploads and treats a page it finds as a replacement. If the tablet cannot be listed
/// nothing is uploaded.</para>
///
/// <para>Only the failures the network can cause are reported; anything else is a bug and propagates with its stack.</para>
/// </summary>
public sealed class MealDocumentDelivery(DeliveryService delivery, IRemarkableClient tablet, IPageInkProbe probe)
{
    private readonly DeliveryService delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
    private readonly IRemarkableClient tablet = tablet ?? throw new ArgumentNullException(nameof(tablet));
    private readonly IPageInkProbe probe = probe ?? throw new ArgumentNullException(nameof(probe));

    /// <summary>A first send: the title is looked up first, and a page already there is a replacement (blank, and only if <paramref name="mayReplaceBlank"/>), never a second document.</summary>
    public Task<PageSent> SendAsync(DeliveryKind kind, DateOnly day, string? subject, byte[] pdf, bool mayReplaceBlank, CancellationToken cancellation) =>
        PutAsync(kind, day, subject, pdf, mayReplaceBlank, cancellation);

    /// <summary>Takes every page under this document's title off the tablet if it is blank, then sends the new one.</summary>
    public Task<PageSent> ReplaceAsync(DeliveryKind kind, DateOnly day, string? subject, byte[] pdf, CancellationToken cancellation) =>
        PutAsync(kind, day, subject, pdf, mayReplaceBlank: true, cancellation);

    private async Task<PageSent> PutAsync(DeliveryKind kind, DateOnly day, string? subject, byte[] pdf, bool mayReplaceBlank, CancellationToken cancellation)
    {
        var name = DeliveredName.For(kind, day, subject);
        var (pages, failure) = await LookUp(name);
        if (pages is null)
        {
            return new PageSent(new DeliveryResult(name, false, failure));
        }

        if (pages.Count > 0)
        {
            var kept = mayReplaceBlank ? await WhyKept(name, pages, cancellation) : InkProbe.Blank;
            if (kept is not null)
            {
                return new PageSent(new DeliveryResult(name, false, null), kept);
            }

            if (await Trash(pages) is { } trashFailure)
            {
                return new PageSent(new DeliveryResult(name, false, trashFailure));
            }
        }

        return new PageSent(await delivery.Deliver(kind, day, subject, pdf));
    }

    // Null when the page may go: it is the only one under the title and the look found it blank.
    private async Task<InkProbe?> WhyKept(string name, IReadOnlyList<RemarkableDoc> pages, CancellationToken cancellation)
    {
        if (pages.Count > 1)
        {
            return InkProbe.Unknown();
        }

        var looked = await probe.ProbeAsync(name, cancellation);
        return looked.Ink == PageInk.Blank ? null : looked;
    }

    private async Task<(IReadOnlyList<RemarkableDoc>? Pages, Exception? Failure)> LookUp(string name)
    {
        try
        {
            var recent = await tablet.ListRecent();
            return (recent.Where(item => string.Equals(item.Title, name, StringComparison.OrdinalIgnoreCase)).ToList(), null);
        }
        catch (HttpRequestException refused)
        {
            return (null, refused);
        }
        catch (TaskCanceledException timedOut) when (timedOut.InnerException is TimeoutException)
        {
            return (null, timedOut);
        }
    }

    private async Task<Exception?> Trash(IReadOnlyList<RemarkableDoc> pages)
    {
        try
        {
            foreach (var page in pages)
            {
                await tablet.MoveToTrash(page.Id);
            }

            return null;
        }
        catch (HttpRequestException refused)
        {
            return refused;
        }
        catch (TaskCanceledException timedOut) when (timedOut.InnerException is TimeoutException)
        {
            return timedOut;
        }
    }
}
