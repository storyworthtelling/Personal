using Android.Content;
using Android.Provider;

namespace Post;

/// <summary>
/// Writes incoming texts into the system SMS provider.
///
/// This only exists because Post holds the SMS role, and it is the least optional thing
/// in the whole feature: once a third-party app is the default, Android stops persisting
/// incoming messages itself and hands that duty entirely to the role holder. An app that
/// takes the role and does not write to the provider silently stops the phone's own
/// record of the owner's texts — and everything received in that window is gone for good,
/// including from any app that reads the provider later.
///
/// The provider is therefore the durable record. Post's timeline cache is a convenience
/// built on top of it and is rebuilt from it, never the other way round.
/// </summary>
internal static class SmsProviderWriter
{
    /// <summary>
    /// Persists the incoming text and returns who it was from and what it said, so the
    /// caller can raise the notification the provider write itself has no opinion about.
    /// Null when the intent carried no message parts.
    /// </summary>
    public static IncomingSms? PersistIncoming(Context context, Intent intent)
    {
        var messages = Telephony.Sms.Intents.GetMessagesFromIntent(intent);
        if (messages is null || messages.Length == 0)
        {
            return null;
        }

        // A long text arrives as several parts of one message; the provider holds one row
        // with the body joined, not one row per part.
        var body = string.Concat(messages.Select(part => part.MessageBody ?? string.Empty));
        var first = messages[0];

        var values = new ContentValues();
        values.Put(Telephony.TextBasedSmsColumns.Address, first.OriginatingAddress);
        values.Put(Telephony.TextBasedSmsColumns.Body, body);
        values.Put(Telephony.TextBasedSmsColumns.Date, Java.Lang.JavaSystem.CurrentTimeMillis());
        values.Put(Telephony.TextBasedSmsColumns.DateSent, first.TimestampMillis);
        // Unread and unseen: Post decides when it has been read, and marking it read here
        // would land every incoming text in the Mailbox already dealt with.
        values.Put(Telephony.TextBasedSmsColumns.Read, 0);
        values.Put(Telephony.TextBasedSmsColumns.Seen, 0);

        context.ContentResolver?.Insert(Telephony.Sms.Inbox.ContentUri!, values);

        return new IncomingSms(first.OriginatingAddress, body)
        {
            SentAt = DateTimeOffset.FromUnixTimeMilliseconds(first.TimestampMillis),
            Parts = messages.Length,
        };
    }
}

internal sealed record IncomingSms(string? Address, string Body)
{
    /// <summary>When the sender's phone sent it, by the carrier's clock; null for an MMS.</summary>
    public DateTimeOffset? SentAt { get; init; }

    public int Parts { get; init; } = 1;
}
