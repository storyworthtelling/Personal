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
                var reading = await ReadAsync(item, promotions, reader);
                OfferAnyDetectedDate(context, item, reading.Event, now);

                if (reading.Kind is null)
                {
                    continue; // genuinely no idea; it waits, like anything unjudged
                }

                database.Cache.Upsert([item with { MailClass = reading.Kind }]);
                HandOverAnyBalance(context, item, reading.Kind.Value);
            }
            catch (Exception e)
            {
                Android.Util.Log.Warn("Post", $"Could not judge {item.SourceId}: {e}");
                PostDiagnostics.LogWarning("Mail", $"Could not judge {item.SourceId}", e);
            }
        }

        AgeOutTrivia(database, now);
        NoteWhatTheDeferredWorkNeeds(context, database);
    }

    // Returns the whole reading, not just the kind. The first version of this took the kind
    // and dropped the event on the floor — the detection ran, cost a token, and produced
    // nothing anywhere, so there was no way to tell whether it worked.
    private static async Task<MailReading> ReadAsync(
        TimelineItem item, SenderPromotions promotions, MailReadingClient? reader)
    {
        var from = item.FromAddress ?? item.ContactKey;
        var signals = new MailSignals(from, item.Subject, HasListUnsubscribe: false);

        var verdict = MailRules.Classify(signals);
        if (verdict.Kind is { } byRule)
        {
            return new MailReading(promotions.Apply(signals, byRule), Event: null);
        }

        // The rules place most machine mail and every bill. What is left is either a real
        // message or a list of unknown kind, and only reading it can say which.
        if (reader is null)
        {
            return new MailReading(
                promotions.Apply(signals, verdict.KnownMachine ? MailClass.Marketing : null), Event: null);
        }

        var reading = await reader.ReadAsync(from, item.Subject, item.Body);
        return reading with { Kind = promotions.Apply(signals, reading.Kind) };
    }

    // A detected date goes two places: onto the correspondence as a proposal the owner can
    // accept, and into the field notes so the detection itself can be judged. Detection that
    // is never accepted is detection that is wrong, and only the second number says so.
    //
    // The day is read here rather than at the fold because this is where the phone is
    // already awake. A calendar that cannot be read is recorded as UNCHECKED, never as
    // clear — the proposal then says so rather than implying the day is free.
    private static void OfferAnyDetectedDate(
        Context context, TimelineItem item, ProposedEvent? detected, DateTimeOffset now)
    {
        if (detected is null)
        {
            return;
        }

        var day = ReadDay(context, detected.StartsOn);
        var offer = ProposedDate.From(detected, day);

        ProposedDates.Offer(context, item, detected, offer.Clashed);
        FieldNotesStore.Record(context, notes =>
            notes.WithDetectedEvent(detected.Title, detected.StartsOn, offer.Clashed, now));
    }

    // Null means nobody looked. The permission is asked for on the way in, but a sync can
    // run before the owner has answered and a policy pass can take it away afterwards, so
    // the refusal is handled rather than assumed away.
    private static IReadOnlyList<Appointment>? ReadDay(Context context, DateOnly day)
    {
        try
        {
            return CalendarAccess.Day(context, day);
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning("Calendar", "Could not read the day for a proposed date", e);
            return null;
        }
    }

    // A balance is Index's business; a bill's amount and due date stay here on the row.
    private static void HandOverAnyBalance(Context context, TimelineItem item, MailClass kind)
    {
        if (kind is not (MailClass.Bill or MailClass.Transactional))
        {
            return;
        }

        var facts = MoneyMail.Read(item.Subject, item.Body);
        var senderName = ResolveSenderName(context, item.ContactKey);
        if (IndexHandoff.PayloadFor(facts, senderName) is { } payload)
        {
            IndexCourier.Send(context, payload);
        }
    }

    // ContactNameResolver already handles the not-in-contacts case gracefully (falls back to
    // the raw key — the email address itself for the common bill/statement sender), but a
    // ContentResolver call can still throw for reasons that have nothing to do with this
    // balance handoff (e.g. no ContentResolver on a torn-down context). A lookup failure here
    // must not break the handoff, so it degrades to no sender name rather than propagating.
    private static string? ResolveSenderName(Context context, string contactKey)
    {
        try
        {
            return ContactNameResolver.DisplayName(context, contactKey);
        }
        catch (Exception e)
        {
            Android.Util.Log.Info("Post", $"Could not resolve sender name for Index handoff: {e.Message}");
            return null;
        }
    }

    // The observations the deferred work cannot be designed without, taken once per sync
    // rather than per message: how large the ghost archive actually grows (is plaintext
    // JSON to Drive on every change sane at this volume?), what the attachments look like,
    // and how many contacts have no photo (which IS the Rolodex pass's justification).
    private static void NoteWhatTheDeferredWorkNeeds(Context context, TimelineDatabase database)
    {
        try
        {
            var ghosts = database.Filing.LoadArchive().All;
            var bytes = ghosts.Sum(ghost => EstimateBytes(ghost.Item));
            FieldNotesStore.Record(context, notes => notes.WithArchive(ghosts.Count, bytes));

            foreach (var item in database.Cache.LoadRecent(limit: 100))
            {
                if (item.Attachments.Count > 0)
                {
                    var attachments = item.Attachments
                        .Select(attachment => (attachment.MimeType, attachment.SizeBytes))
                        .ToList();
                    FieldNotesStore.Record(context, notes =>
                        notes.WithMail(hasTextPart: true, hasHtmlPart: false, attachments));
                }
            }

            var (total, withoutPhoto) = ContactPicker.PhotoCoverage(context);
            FieldNotesStore.Record(context, notes => notes.WithContacts(total, withoutPhoto));
        }
        catch (Exception e)
        {
            // Observations are never worth a failure.
            PostDiagnostics.LogWarning("FieldNotes", "Could not take the periodic observations", e);
        }
    }

    // Close enough to size a Drive backup: the ghost is stored as serialized JSON, and what
    // is wanted is an order of magnitude rather than a byte count.
    private static long EstimateBytes(TimelineItem item) =>
        (item.Subject?.Length ?? 0) + (item.Body?.Length ?? 0) + (item.Snippet?.Length ?? 0) + 200;

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
