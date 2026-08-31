using Android.Content;
using Android.Provider;
using Android.Telephony;

namespace Post;

/// <summary>
/// Sends a text as the role holder, and — just as importantly — records it.
///
/// Android hands the default SMS app both halves of the job. It transmits what
/// <see cref="SmsManager"/> is given, but it does <b>not</b> write the sent message into
/// the system store; that is the role holder's duty, exactly as it is for incoming mail.
/// Skip it and the text goes out and then vanishes: it is in the recipient's phone and in
/// nothing on the owner's, which is worse than not sending it, because the owner has no
/// way to know.
/// </summary>
internal static class SmsSender
{
    public static void Send(Context context, string number, string text)
    {
        var manager = SmsManager.Default
            ?? throw new InvalidOperationException("This device has no SMS manager.");

        // A long message has to be divided or the transmit silently truncates it.
        var parts = manager.DivideMessage(text);
        if (parts is null || parts.Count <= 1)
        {
            manager.SendTextMessage(number, null, text, null, null);
        }
        else
        {
            manager.SendMultipartTextMessage(number, null, parts, null, null);
        }

        Record(context, number, text);
    }

    private static void Record(Context context, string number, string text)
    {
        var values = new ContentValues();
        values.Put(Telephony.TextBasedSmsColumns.Address, number);
        values.Put(Telephony.TextBasedSmsColumns.Body, text);
        values.Put(Telephony.TextBasedSmsColumns.Date, Java.Lang.JavaSystem.CurrentTimeMillis());
        values.Put(Telephony.TextBasedSmsColumns.Read, 1);
        values.Put(Telephony.TextBasedSmsColumns.Seen, 1);

        context.ContentResolver?.Insert(Telephony.Sms.Sent.ContentUri!, values);
    }
}
