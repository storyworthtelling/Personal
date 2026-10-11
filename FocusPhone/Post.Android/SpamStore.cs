using Android.Content;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// The owner's spam judgements, kept on the phone in the same shape as the sender
/// promotions (a string set in its own preferences file). The rules are in
/// <see cref="SpamList"/>; this only carries them between launches.
/// </summary>
internal static class SpamStore
{
    private const string PreferencesName = "spam";
    private const string EntriesKey = "entries";

    public static SpamList Load(Context context)
    {
        var stored = Prefs(context).GetStringSet(EntriesKey, null);
        return SpamList.From(stored ?? []);
    }

    public static void Save(Context context, SpamList spam) =>
        Prefs(context).Edit()!
            .PutStringSet(EntriesKey, spam.ToStored().ToList())!
            .Apply();

    private static ISharedPreferences Prefs(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
