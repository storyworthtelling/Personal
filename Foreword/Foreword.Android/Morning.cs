using Microsoft.Extensions.DependencyInjection;
using Foreword.Domain.Calendar;
using Foreword.Domain.Delivery;
using Foreword.Infrastructure.Delivery;
using Foreword.Infrastructure.Prayer;

namespace Foreword.Android;

/// <summary>
/// The morning's documents as one <see cref="MorningRun"/>, for the delivery worker and the Deliver now button alike:
/// the brief, the first-Sunday bonus puzzle, The Day and prayer, each sent at most once a day and reported on its own
/// (audit 11 §6–7). The outcome goes to the status line; the run's caller decides whether to go round again.
/// </summary>
internal static class Morning
{
    public static async Task<MorningOutcome> Deliver(IServiceProvider services, DateOnly today)
    {
        var crossword = services.GetRequiredService<CrosswordDeliveryService>();
        var google = CredentialStore.IsGoogleConfigured;
        var daily = google ? services.GetRequiredService<DailyDeliveryService>() : null;
        var prayer = google ? services.GetRequiredService<PrayerDeliveryService>() : null;

        MorningStep[] steps =
        [
            new(MorningDocument.Brief, async () => await crossword.DeliverDailyPuzzle(today)),
            new(MorningDocument.Bonus, CrosswordDeliveryService.IsFirstSundayOfMonth(today)
                ? async () => { await crossword.DeliverBonusPuzzle(today); return null; }
                : null),
            new(MorningDocument.Day, daily is null ? null : async () =>
            {
                await daily.EnsureColumnCalendars();
                var day = await daily.DeliverDailyDocuments(today);
                CredentialStore.UnreadableCalendars = UnreadableCalendarNotice.Store(day.UnreadableCalendars);
                if (!day.Uploaded)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(
                        day.Failure ?? new InvalidOperationException("The Day did not reach the tablet."));
                }
                return null;
            }),
            // Prayer keeps its own outcome line as well: it reads a file the rest of the morning does not, behind a
            // passphrase the rest does not need, so its reason is worth a line of its own.
            new(MorningDocument.Prayer, prayer is null ? null : async () =>
            {
                try
                {
                    await prayer.Deliver(today);
                    CredentialStore.LastPrayerOutcome = "Delivered";
                    return null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    CredentialStore.LastPrayerOutcome = $"Failed: {MorningRun.Reason(ex)}";
                    throw;
                }
            }),
        ];

        var outcome = await new MorningRun(() => CredentialStore.DeliveredToday, value => CredentialStore.DeliveredToday = value)
            .Deliver(today, steps);
        CredentialStore.LastDeliveryOutcome = outcome.Summary();
        CredentialStore.LastDeliveryAtUtc = DateTime.UtcNow;
        return outcome;
    }
}
