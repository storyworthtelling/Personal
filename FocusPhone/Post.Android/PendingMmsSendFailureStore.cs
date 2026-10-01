using Android.Content;

namespace Post;

/// <summary>
/// Where "the last MMS send failed" waits between <see cref="MmsSendReceiver"/>, which is
/// the only thing that knows — sending is asynchronous, so the compose dialog is long closed
/// by the time an outcome exists — and the Mailbox, which can surface it on next open. One
/// slot, matching <see cref="PendingCallNoteStore"/>: a second failure before the owner has
/// seen the first just replaces it, since the notice's job is "something needs resending,"
/// not a tally.
/// </summary>
internal static class PendingMmsSendFailureStore
{
    private const string PreferencesName = "pendingMmsSendFailure";
    private const string FailedKey = "failed";

    public static void MarkFailed(Context context) =>
        Prefs(context).Edit()!.PutBoolean(FailedKey, true)!.Apply();

    /// <summary>Reads and clears in one step: the notice is shown once, not re-shown every resume.</summary>
    public static bool TakeFailed(Context context)
    {
        var prefs = Prefs(context);
        var failed = prefs.GetBoolean(FailedKey, false);
        if (failed)
        {
            prefs.Edit()!.Clear()!.Apply();
        }
        return failed;
    }

    private static ISharedPreferences Prefs(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
