using System.Collections.Concurrent;
using Android.App;
using Android.Content;
using Android.Provider;
using Android.Telephony;
using Codex.Domain.Diagnostics;
using Codex.Domain.Post;

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
///
/// Each part carries a sent intent (LOG-LOOP-PLAN.md §4.5), so the radio's result code reaches the log
/// through <see cref="SmsSentReceiver"/> and the send's key action learns how it ended.
/// </summary>
internal static class SmsSender
{
    internal const string ActionSent = "com.storyworthtelling.focusphone.communication.SMS_SENT";
    internal const string ExtraToken = "com.storyworthtelling.focusphone.communication.extra.SMS_TOKEN";

    // A send's key action, by token, until every part has reported. In memory only: if Android reclaims
    // the process mid-send the result is still logged by the receiver; only the tap verdict is lost.
    private static readonly ConcurrentDictionary<string, (KeyAction Action, int Remaining)> Pending = new();

    public static void Send(Context context, string number, string text, KeyAction action)
    {
        var manager = SmsManager.Default
            ?? throw new InvalidOperationException("This device has no SMS manager.");

        // A long message has to be divided or the transmit silently truncates it.
        var parts = manager.DivideMessage(text);
        var count = parts is null || parts.Count <= 1 ? 1 : parts.Count;
        var token = Guid.NewGuid().ToString("N");
        Pending[token] = (action, count);
        var intents = Enumerable.Range(0, count).Select(i => SentIntent(context, token, i)).ToList();

        if (count == 1)
        {
            manager.SendTextMessage(number, null, text, intents[0], null);
        }
        else
        {
            manager.SendMultipartTextMessage(number, null, parts!, intents, null);
        }

        Record(context, number, text);
    }

    /// <summary>One part's result arrived: a failure ends the send, the last success completes it.</summary>
    internal static void PartSent(string? token, int resultCode)
    {
        if (token is null || !Pending.TryGetValue(token, out var entry))
        {
            return;
        }
        if (resultCode != PostLogLines.ResultOk)
        {
            Pending.TryRemove(token, out _);
            entry.Action.Failed(PostLogLines.SmsSent(resultCode));
            return;
        }
        if (entry.Remaining <= 1)
        {
            Pending.TryRemove(token, out _);
            entry.Action.Succeeded();
        }
        else
        {
            Pending[token] = (entry.Action, entry.Remaining - 1);
        }
    }

    private static PendingIntent SentIntent(Context context, string token, int part)
    {
        var intent = new Intent(context, typeof(SmsSentReceiver))
            .SetAction(ActionSent)
            .PutExtra(ExtraToken, token);
        return PendingIntent.GetBroadcast(
            context, HashCode.Combine(token, part), intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
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

/// <summary>
/// The radio's answer for each sent part (<c>SmsManager</c>'s sent intent): logged with its named result
/// code, and reported to the send's key action.
/// </summary>
[BroadcastReceiver(
    Name = "com.storyworthtelling.focusphone.communication.SmsSentReceiver",
    Exported = false)]
public class SmsSentReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        try
        {
            var code = (int)ResultCode;
            var line = PostLogLines.SmsSent(code);
            if (code == PostLogLines.ResultOk)
            {
                PostDiagnostics.LogInfo("Sms", line);
            }
            else
            {
                PostDiagnostics.LogWarning("Sms", line);
            }
            SmsSender.PartSent(intent?.GetStringExtra(SmsSender.ExtraToken), code);
        }
        catch (Exception e)
        {
            // Never throw out of a receiver: the text already went (or did not) either way.
            Android.Util.Log.Warn("Post", $"Could not record an SMS send result: {e}");
        }
    }
}
