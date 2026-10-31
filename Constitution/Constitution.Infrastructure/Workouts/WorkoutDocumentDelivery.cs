using Constitution.Domain.Delivery;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Delivery;
using Storyworthtelling.Remarkable;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// What a send or a replace did: the delivery's own result, or, when the page was left where it was, <see cref="Kept"/> and why.
/// </summary>
/// <param name="Result">The delivery's result; when the page was kept it is a result of nothing delivered and no failure.</param>
/// <param name="Kept">Set when the page on the tablet was left alone: the look found ink on it, could not say, or (a first send) the slot had begun.</param>
public sealed record PageSent(DeliveryResult Result, InkProbe? Kept = null)
{
    public bool Delivered => Result.Delivered;

    public string Name => Result.Name;

    public Exception? Failure => Result.Failure;
}

/// <summary>What a withdrawal did: the failure that stopped it, or <see cref="Kept"/> when the page was left where it was.</summary>
public sealed record PageWithdrawn(Exception? Failure, InkProbe? Kept = null);

/// <summary>
/// Puts a workout page on the tablet, replaces one, or takes one away (C-2.3.6), over <see cref="DeliveryService"/> so a
/// refused token is still the same Problem notice and the same retry.
///
/// <para><b>It looks before it takes a page off</b> (S3). The read-back reads a blank page as a skip, so trashing a page the owner has
/// begun writing on, or setting a blank copy down over it, loses the session and records a skip they never made. Before any page is
/// trashed it is asked about (<see cref="IPageInkProbe"/>): only <see cref="PageInk.Blank"/> lets it go, and ink on it, or no way to say,
/// leaves it exactly where it is. More than one page under the title cannot be looked at as one, so it is left as well.</para>
///
/// <para><b>It never makes a second document</b>. The cloud does not replace by title: a second upload under the same name is a second
/// document, and the read-back finds a page by title and would meet either. A replacement therefore takes the pages under the title off
/// first, and a <i>first</i> send, which has no record of what is on the tablet (the record may have been set aside), looks the title up
/// before it uploads and treats a page it finds as a replacement. A first send may replace a blank page only while the slot has not begun
/// (<c>mayReplaceBlank</c>); once it has, a page it cannot account for stays. If the tablet cannot be listed nothing is uploaded.</para>
///
/// <para>Only the failures the network can cause (<see cref="HttpRequestException"/>, a timeout) are reported; anything else
/// is a bug and propagates with its stack.</para>
/// </summary>
public sealed class WorkoutDocumentDelivery(DeliveryService delivery, IRemarkableClient tablet, IPageInkProbe probe)
{
    private readonly DeliveryService delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
    private readonly IRemarkableClient tablet = tablet ?? throw new ArgumentNullException(nameof(tablet));
    private readonly IPageInkProbe probe = probe ?? throw new ArgumentNullException(nameof(probe));

    /// <summary>A first send: the title is looked up first, and a page already there is a replacement (blank, and only if <paramref name="mayReplaceBlank"/>), never a second document.</summary>
    public Task<PageSent> SendAsync(DateOnly day, string subject, byte[] pdf, bool mayReplaceBlank = false, CancellationToken cancellation = default) =>
        PutAsync(day, subject, pdf, mayReplaceBlank, cancellation);

    /// <summary>Takes every page under this document's title off the tablet if it is blank, then sends the new one.</summary>
    public Task<PageSent> ReplaceAsync(DateOnly day, string subject, byte[] pdf, CancellationToken cancellation = default) =>
        PutAsync(day, subject, pdf, mayReplaceBlank: true, cancellation);

    /// <summary>Takes the page away if it is blank. A null failure and no kept page means it is gone (including when it never was there).</summary>
    public async Task<PageWithdrawn> WithdrawAsync(DateOnly day, string subject, CancellationToken cancellation = default)
    {
        var name = DeliveredName.For(DeliveryKind.Workout, day, subject);
        var (pages, failure) = await LookUp(name);
        if (pages is null)
        {
            return new PageWithdrawn(failure);
        }

        if (pages.Count == 0)
        {
            return new PageWithdrawn(null);
        }

        if (await WhyKept(name, pages, cancellation) is { } kept)
        {
            return new PageWithdrawn(null, kept);
        }

        return new PageWithdrawn(await Trash(pages));
    }

    private async Task<PageSent> PutAsync(DateOnly day, string subject, byte[] pdf, bool mayReplaceBlank, CancellationToken cancellation)
    {
        var name = DeliveredName.For(DeliveryKind.Workout, day, subject);
        var (pages, failure) = await LookUp(name);
        if (pages is null)
        {
            return new PageSent(new DeliveryResult(name, false, failure));
        }

        if (pages.Count > 0)
        {
            // A page the owner has not touched may be replaced; one the record cannot account for, once the slot has begun, is left.
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

        return new PageSent(await delivery.Deliver(DeliveryKind.Workout, day, subject, pdf));
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
