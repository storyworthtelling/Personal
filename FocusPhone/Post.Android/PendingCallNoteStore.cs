using Android.Content;

namespace Post;

/// <summary>
/// Where "a call worth a note just ended" waits between <see cref="CallStateReceiver"/>,
/// which noticed it, and the shell, which can safely ask about it. A call can end while
/// Post is nowhere on screen — sometimes with the process not even running — so nothing
/// here may pull the app to the foreground (§5-§11): the prompt waits behind the dog-ear
/// for the next time the owner opens Post themselves, exactly like the SMS role's own
/// waiting notice.
///
/// One slot, not a queue: a second call landing before the first note is written simply
/// replaces it, matching every other single-fold notice in this app (§13's "a fold means
/// something waits, not how much").
/// </summary>
internal static class PendingCallNoteStore
{
    private const string PreferencesName = "pendingCallNote";
    private const string ContactKeyKey = "contactKey";
    private const string NumberKey = "number";
    private const string DisplayNameKey = "displayName";

    public sealed record Pending(string ContactKey, string Number, string DisplayName);

    public static void Set(Context context, string contactKey, string number, string displayName) =>
        Prefs(context).Edit()!
            .PutString(ContactKeyKey, contactKey)!
            .PutString(NumberKey, number)!
            .PutString(DisplayNameKey, displayName)!
            .Apply();

    public static Pending? Get(Context context)
    {
        var prefs = Prefs(context);
        var contactKey = prefs.GetString(ContactKeyKey, null);
        var number = prefs.GetString(NumberKey, null);
        var displayName = prefs.GetString(DisplayNameKey, null);
        return contactKey is null || number is null || displayName is null
            ? null
            : new Pending(contactKey, number, displayName);
    }

    public static void Clear(Context context) => Prefs(context).Edit()!.Clear()!.Apply();

    private static ISharedPreferences Prefs(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
