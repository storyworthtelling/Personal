using Constitution.Domain.Delivery;
using Constitution.Infrastructure.Delivery;
using Storyworthtelling.Remarkable;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// Puts a workout page on the tablet, replaces one, or takes one away (C-2.3.6), over <see cref="DeliveryService"/> so a
/// refused token is still the same Problem notice and the same retry.
///
/// <para><b>Replacing is trash first, then upload.</b> The cloud does not replace by title: a second upload under the same
/// name is a second document, and the read-back finds a page by title and would meet the old one. So the pages already under
/// that title are taken off, and only then is the new one sent. If a page cannot be taken off nothing is sent (never two); if
/// the upload fails after the trash the delivery service keeps the page to retry and the notice is raised, and because the
/// caller's record of what is on the tablet is not updated until a delivery succeeds, the next run replaces again. The
/// title is matched without case, as the cloud's own lookup matches it.</para>
///
/// <para>Only the failures the network can cause (<see cref="HttpRequestException"/>, a timeout) are reported; anything else
/// is a bug and propagates with its stack.</para>
/// </summary>
public sealed class WorkoutDocumentDelivery(DeliveryService delivery, IRemarkableClient tablet)
{
    private readonly DeliveryService delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
    private readonly IRemarkableClient tablet = tablet ?? throw new ArgumentNullException(nameof(tablet));

    /// <summary>A first send: one upload at the root, nothing else asked of the tablet.</summary>
    public Task<DeliveryResult> SendAsync(DateOnly day, string subject, byte[] pdf) =>
        delivery.Deliver(DeliveryKind.Workout, day, subject, pdf);

    /// <summary>Takes every page under this document's title off the tablet, then sends the new one.</summary>
    public async Task<DeliveryResult> ReplaceAsync(DateOnly day, string subject, byte[] pdf)
    {
        var name = DeliveredName.For(DeliveryKind.Workout, day, subject);
        if (await TrashPagesTitled(name) is { } failure)
        {
            return new DeliveryResult(name, false, failure);
        }

        return await delivery.Deliver(DeliveryKind.Workout, day, subject, pdf);
    }

    /// <summary>Takes the page away. Null when it is gone (including when it never was there); the failure otherwise.</summary>
    public Task<Exception?> WithdrawAsync(DateOnly day, string subject) =>
        TrashPagesTitled(DeliveredName.For(DeliveryKind.Workout, day, subject));

    private async Task<Exception?> TrashPagesTitled(string name)
    {
        try
        {
            var pages = await tablet.ListRecent();
            foreach (var page in pages.Where(item => string.Equals(item.Title, name, StringComparison.OrdinalIgnoreCase)))
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
