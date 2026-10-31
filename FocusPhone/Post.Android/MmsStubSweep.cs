using Android.Content;
using Android.Provider;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// Asks again for MMS whose download failed. The carrier announces a message once and holds
/// it until it expires, so a download that failed for an ordinary reason — no mobile data at
/// that moment, the carrier slow to answer — would otherwise be lost for good, leaving only
/// the placeholder row. Which placeholders are due is <see cref="MmsStubRetry"/>'s call; this
/// reads them, counts the tries, and starts the downloads. Runs when Post starts and with
/// every background sync.
/// </summary>
internal static class MmsStubSweep
{
    private const string PreferencesName = "mmsRetries";
    private const int MessageTypeNotificationInd = 0x82;

    public static void Run(Context context)
    {
        UnreadNoticeStore.Retry(context);
        try
        {
            var stubs = ReadStubs(context);
            if (stubs.Count == 0)
            {
                return;
            }
            var attempts = Attempts(context);
            foreach (var stub in MmsStubRetry.Due(stubs, attempts, DateTimeOffset.UtcNow))
            {
                // A failure is counted by the download receiver when it hears of it, so a retry
                // is not counted twice.
                var uri = Android.Net.Uri.Parse($"content://mms/{stub.Id}")!;
                PostDiagnostics.LogText("Mms", $"Retrying an MMS download after {attempts.GetValueOrDefault(stub.Id)} failed");
                MmsDownloader.Start(context, uri, stub.TransactionId!, stub.ContentLocation!);
            }
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning("Mms", "Could not retry stuck MMS downloads", e);
        }
    }

    /// <summary>Counts a failed try against a placeholder, so a message the carrier will never give up is left alone after three.</summary>
    public static void RecordAttempt(Context context, Android.Net.Uri? stub)
    {
        if (stub?.LastPathSegment is not { } id)
        {
            return;
        }
        var preferences = Prefs(context);
        preferences.Edit()!.PutInt(id, preferences.GetInt(id, 0) + 1)!.Apply();
    }

    public static void Forget(Context context, Android.Net.Uri stub)
    {
        if (stub.LastPathSegment is { } id)
        {
            Prefs(context).Edit()!.Remove(id)!.Apply();
        }
    }

    private static Dictionary<long, int> Attempts(Context context)
    {
        var attempts = new Dictionary<long, int>();
        foreach (var (key, value) in Prefs(context).All ?? new Dictionary<string, object>())
        {
            if (long.TryParse(key, out var id) && value is Java.Lang.Integer count)
            {
                attempts[id] = count.IntValue();
            }
        }
        return attempts;
    }

    private static List<MmsStub> ReadStubs(Context context)
    {
        var stubs = new List<MmsStub>();
        using var cursor = context.ContentResolver?.Query(
            Telephony.Mms.ContentUri!, ["_id", "tr_id", "ct_l", "date", "exp"],
            "m_type = ? AND msg_box = ?", [MessageTypeNotificationInd.ToString(), SmsLogReader.MmsBoxInbox.ToString()], null);
        while (cursor is not null && cursor.MoveToNext())
        {
            stubs.Add(new MmsStub(
                cursor.GetLong(0),
                cursor.IsNull(1) ? null : cursor.GetString(1),
                cursor.IsNull(2) ? null : cursor.GetString(2),
                cursor.GetLong(3),
                cursor.IsNull(4) || cursor.GetLong(4) == 0 ? null : cursor.GetLong(4)));
        }
        return stubs;
    }

    private static ISharedPreferences Prefs(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
