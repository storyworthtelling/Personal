using Android.Content;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Email;

namespace Post;

/// <summary>
/// The pass that sorts mail, ages out trivia, and hands balances to Index. It runs inside
/// the sync rather than on its own schedule (T29) — the messages are already in hand, the
/// phone is already awake, and a second worker would be a second thing to go wrong.
///
/// The order is deliberate: rules first, the model only for what the rules could not
/// place, and the owner's corrections over everything. That keeps most of the owner's
/// correspondence off the network entirely and makes an absent key a degradation rather
/// than a failure.
///
/// Nothing here may throw. It runs after mail has arrived and before nothing at all — a
/// classification that fails should leave the message unsorted, never leave the sync
/// broken.
/// </summary>
internal static class MailIntelligence
{
    public static async Task RunAsync(
        Context context, TimelineDatabase database, MailReadingClient? reader, DateTimeOffset now)
    {
        var promotions = SenderPromotionStore.Load(context);

        foreach (var item in database.Cache.LoadRecent(limit: 300))
        {
            if (item.Kind != TimelineItemKind.Email || item.MailClass is not null)
            {
                continue; // already judged, or not mail
            }

            try
            {
                var kind = await ClassifyAsync(item, promotions, reader);
                if (kind is null)
                {
                    continue; // genuinely no idea; it waits, like anything unjudged
                }

                database.Cache.Upsert([item with { MailClass = kind }]);
                HandOverAnyBalance(context, item, kind.Value);
            }
            catch (Exception e)
            {
                Android.Util.Log.Warn("Post", $"Could not judge {item.SourceId}: {e}");
                PostDiagnostics.LogWarning("Mail", $"Could not judge {item.SourceId}", e);
            }
        }

        AgeOutTrivia(database, now);
    }

    private static async Task<MailClass?> ClassifyAsync(
        TimelineItem item, SenderPromotions promotions, MailReadingClient? reader)
    {
        var from = item.FromAddress ?? item.ContactKey;
        var signals = new MailSignals(from, item.Subject, HasListUnsubscribe: false);

        var verdict = MailRules.Classify(signals);
        if (verdict.Kind is { } byRule)
        {
            return promotions.Apply(signals, byRule);
        }

        // The rules place most machine mail and every bill. What is left is either a real
        // message or a list of unknown kind, and only reading it can say which.
        if (reader is null)
        {
            return promotions.Apply(signals, verdict.KnownMachine ? MailClass.Marketing : null);
        }

        var reading = await reader.ReadAsync(from, item.Subject, item.Body);
        return promotions.Apply(signals, reading.Kind);
    }

    // A balance is Index's business; a bill's amount and due date stay here on the row.
    private static void HandOverAnyBalance(Context context, TimelineItem item, MailClass kind)
    {
        if (kind is not (MailClass.Bill or MailClass.Transactional))
        {
            return;
        }

        var facts = MoneyMail.Read(item.Subject, item.Body);
        if (IndexHandoff.PayloadFor(facts) is { } payload)
        {
            IndexCourier.Send(context, payload);
        }
    }

    // Aged-out trivia is GONE, not filed. Filing is for things the owner dealt with, and a
    // marketing email nobody opened was never dealt with — putting it in the archive would
    // make Filed a landfill instead of a record.
    private static void AgeOutTrivia(TimelineDatabase database, DateTimeOffset now)
    {
        foreach (var item in database.Cache.LoadRecent(limit: 500))
        {
            if (item.MailClass is { } kind && MailAgeing.HasExpired(kind, item.TimestampUtc, now))
            {
                database.Cache.Delete(item.Kind, item.SourceId);
            }
        }
    }
}
