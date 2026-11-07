using Android.Content;
using Android.Provider;

namespace WatchSpike;

/// <summary>
/// The four components Android requires before it will even offer an app the SMS role, copied in
/// shape from Post's (FocusPhone/Post.Android/SmsRoleComponents.cs). The spike keeps only who sent
/// a text and when; the text itself goes into the watch's own message store, as any default SMS
/// app must, and is never read, logged or shown here.
/// </summary>
[BroadcastReceiver(Name = "com.storyworthtelling.watchspike.SpikeSmsReceiver", Exported = true, Permission = "android.permission.BROADCAST_SMS")]
[IntentFilter(["android.provider.Telephony.SMS_DELIVER"])]
public class SpikeSmsReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        try
        {
            var parts = Telephony.Sms.Intents.GetMessagesFromIntent(intent);
            if (parts is null || parts.Length == 0)
            {
                SpikeLog.Note(context, "A text arrived with nothing readable in it.");
                return;
            }

            var address = parts[0]?.OriginatingAddress ?? string.Empty;
            var body = string.Concat(parts.Select(p => p?.MessageBody ?? string.Empty));
            var values = new ContentValues();
            values.Put(Telephony.ITextBasedSmsColumns.Address, address);
            values.Put(Telephony.ITextBasedSmsColumns.Body, body);
            values.Put(Telephony.ITextBasedSmsColumns.Date, Java.Lang.JavaSystem.CurrentTimeMillis());
            values.Put(Telephony.ITextBasedSmsColumns.Read, 0);
            context.ContentResolver?.Insert(Telephony.Sms.Inbox.ContentUri!, values);
            SpikeLog.TextArrived(context, address);
        }
        catch (Exception e)
        {
            SpikeLog.Note(context, $"A text arrived but could not be stored: {e.GetType().Name}.");
        }
    }
}

[BroadcastReceiver(Name = "com.storyworthtelling.watchspike.SpikeMmsReceiver", Exported = true, Permission = "android.permission.BROADCAST_WAP_PUSH")]
[IntentFilter(["android.provider.Telephony.WAP_PUSH_DELIVER"], DataMimeType = "application/vnd.wap.mms-message")]
public class SpikeMmsReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null)
        {
            SpikeLog.Note(context, "An MMS notice arrived (picture or group text). The spike does not download it.");
        }
    }
}

[Service(Name = "com.storyworthtelling.watchspike.SpikeRespondService", Exported = true, Permission = "android.permission.SEND_RESPOND_VIA_MESSAGE")]
[IntentFilter(["android.intent.action.RESPOND_VIA_MESSAGE"], Categories = [Intent.CategoryDefault], DataSchemes = ["sms", "smsto", "mms", "mmsto"])]
public class SpikeRespondService : Android.App.Service
{
    public override Android.OS.IBinder? OnBind(Intent? intent) => null;
}

[Android.App.Activity(Name = "com.storyworthtelling.watchspike.SpikeSendToActivity", Exported = true, NoHistory = true)]
[IntentFilter([Intent.ActionSendto, Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataSchemes = ["sms", "smsto", "mms", "mmsto"])]
public class SpikeSendToActivity : Android.App.Activity
{
    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        StartActivity(new Intent(this, typeof(MainActivity)));
        Finish();
    }
}
