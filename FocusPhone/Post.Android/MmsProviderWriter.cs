using Android.Content;
using Android.Provider;
using Android.Telephony;
using Codex.Domain.Post;

namespace Post;

/// <summary>A received MMS once it is in the provider: its row id and what was written.</summary>
internal sealed record StoredMms(string Id, MmsInboxRecord Record);

/// <summary>
/// Writes MMS into the system provider the way AOSP's PduPersister does — the message row,
/// then its parts under <c>content://mms/&lt;id&gt;/part</c>, then its addresses under
/// <c>content://mms/&lt;id&gt;/addr</c> — because as the default SMS app nobody else will.
///
/// Parts go under the message's own Uri: the provider's insert accepts a part only there
/// (its <c>content://mms/part</c> route is read-only and answers an insert with nothing), and
/// it makes the part's file itself for anything that is not text or SMIL. Addresses go last,
/// so a reload that lands mid-write sees a message with no sender, which the reader skips,
/// rather than one with half its content.
/// </summary>
internal static class MmsProviderWriter
{
    private const int MmsBoxInbox = 1;
    private const int Utf8 = 106;

    /// <summary>
    /// Stores a downloaded message in the thread Android keeps for its sender and the other
    /// recipients, the same thread a group reply resolves to. Null when the message is
    /// already stored — the carrier announced it twice.
    /// </summary>
    public static StoredMms? StoreIncoming(Context context, RetrievedMms message, DateTimeOffset receivedAt)
    {
        if (!string.IsNullOrEmpty(message.MessageId) && AlreadyStored(context, message.MessageId))
        {
            return null;
        }

        var record = MmsInboxRecord.From(message, receivedAt);
        var recipients = message.ThreadRecipients(OwnNumbers.Read(context));

        var values = new ContentValues();
        values.Put(Telephony.IBaseMmsColumns.MessageBox, MmsBoxInbox);
        values.Put(Telephony.IBaseMmsColumns.MessageType, MmsInboxRecord.MessageTypeRetrieveConf);
        values.Put(Telephony.IBaseMmsColumns.Date, record.DateSeconds);
        if (record.DateSentSeconds is { } sent)
        {
            values.Put(Telephony.IBaseMmsColumns.DateSent, sent);
        }
        values.Put(Telephony.IBaseMmsColumns.Read, 0);
        values.Put(Telephony.IBaseMmsColumns.Seen, 0);
        values.Put(Telephony.IBaseMmsColumns.TextOnly, record.TextOnly ? 1 : 0);
        if (recipients.Count > 0)
        {
            values.Put(Telephony.IBaseMmsColumns.ThreadId, Telephony.Threads.GetOrCreateThreadId(context, recipients.ToList()));
        }
        PutIfSet(values, Telephony.IBaseMmsColumns.Subject, record.Subject);
        if (record.Subject is not null)
        {
            values.Put(Telephony.IBaseMmsColumns.SubjectCharset, Utf8);
        }
        PutIfSet(values, Telephony.IBaseMmsColumns.MessageId, record.MessageId);
        PutIfSet(values, Telephony.IBaseMmsColumns.TransactionId, record.TransactionId);
        PutIfSet(values, Telephony.IBaseMmsColumns.ContentType, record.ContentType);
        if (record.MmsVersion is { } version)
        {
            values.Put(Telephony.IBaseMmsColumns.MmsVersion, version);
        }

        var messageUri = context.ContentResolver?.Insert(Telephony.Mms.Inbox.ContentUri!, values)
            ?? throw new InvalidOperationException("The MMS provider refused the incoming message row.");
        var id = messageUri.LastPathSegment
            ?? throw new InvalidOperationException($"The MMS provider returned a row with no id: {messageUri}.");

        foreach (var part in record.Parts)
        {
            InsertPart(context, id, part);
        }
        foreach (var address in record.Addresses)
        {
            InsertAddress(context, id, address.Address, address.Type);
        }
        return new StoredMms(id, record);
    }

    /// <summary>
    /// One part under its message. Text is inline; anything else is written to the file the
    /// provider made for it on insert.
    /// </summary>
    public static void InsertPart(Context context, string messageId, MmsPartRow part)
    {
        var values = new ContentValues();
        values.Put("ct", part.ContentType);
        values.Put("chset", part.Charset);
        PutIfSet(values, "name", part.Name);
        PutIfSet(values, "fn", part.Name);
        PutIfSet(values, "cl", part.ContentLocation);
        PutIfSet(values, "cid", part.ContentId);
        if (part.Text is not null)
        {
            values.Put("text", part.Text);
        }

        var partUri = context.ContentResolver?.Insert(Android.Net.Uri.Parse($"content://mms/{messageId}/part")!, values)
            ?? throw new InvalidOperationException($"The MMS provider refused a {part.ContentType} part.");
        if (part.Data is null)
        {
            return;
        }
        using var stream = context.ContentResolver!.OpenOutputStream(partUri)
            ?? throw new InvalidOperationException($"The MMS provider gave no file for a {part.ContentType} part.");
        stream.Write(part.Data, 0, part.Data.Length);
    }

    public static void InsertAddress(Context context, string messageId, string address, int type)
    {
        var values = new ContentValues();
        values.Put("address", address);
        values.Put("type", type);
        values.Put("charset", Utf8);
        context.ContentResolver?.Insert(Android.Net.Uri.Parse($"content://mms/{messageId}/addr")!, values);
    }

    private static bool AlreadyStored(Context context, string messageId)
    {
        using var cursor = context.ContentResolver?.Query(
            Telephony.Mms.ContentUri!, ["_id"], "m_id = ? AND msg_box = ?",
            [messageId, MmsBoxInbox.ToString()], null);
        return cursor is not null && cursor.MoveToFirst();
    }

    private static void PutIfSet(ContentValues values, string column, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            values.Put(column, value);
        }
    }
}

/// <summary>
/// This phone's own number(s), so a group's members can leave it out. The default SMS app may
/// read the line number without a permission of its own; a SIM that does not carry its
/// number gives nothing, and then nobody is left out.
/// </summary>
internal static class OwnNumbers
{
    // Read once per process: the timeline reload asks on every pass, and a SIM's number
    // does not change under a running app.
    private static IReadOnlyList<string>? known;
    private static bool refusalLogged;

    // A refusal is not cached, so the number is read as soon as the permission is granted;
    // it is logged once per process rather than on every reload.
    public static IReadOnlyList<string> Read(Context context) => known ?? ReadFromSim(context);

    private static IReadOnlyList<string> ReadFromSim(Context context)
    {
        var numbers = new List<string>();
        try
        {
            var subscriptions = (SubscriptionManager?)context.GetSystemService(Context.TelephonySubscriptionService);
            foreach (var info in subscriptions?.ActiveSubscriptionInfoList ?? [])
            {
                var number = OperatingSystem.IsAndroidVersionAtLeast(33)
                    ? subscriptions!.GetPhoneNumber(info.SubscriptionId)
                    : info.Number;
                if (!string.IsNullOrWhiteSpace(number))
                {
                    numbers.Add(number);
                }
            }
        }
        catch (Java.Lang.SecurityException e)
        {
            if (!refusalLogged)
            {
                refusalLogged = true;
                PostDiagnostics.LogWarning("Mms", "Could not read this phone's number; group members will include it", e);
            }
            return numbers;
        }
        known = numbers;
        return numbers;
    }
}
