using Android.Content;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// What each group conversation is called, kept on the phone. The owner's names and the
/// names learned from Google Messages are separate keys, so the notification listener
/// learning a title can never write over a name the owner has just given.
/// </summary>
internal static class GroupNameStore
{
    private const string PreferencesName = "groupNames";
    private const string NamedKey = "named";
    private const string LearnedKey = "learned";

    public static GroupNames Load(Context context)
    {
        var preferences = Prefs(context);
        return GroupNames.From(
            preferences.GetStringSet(NamedKey, null) ?? [],
            preferences.GetStringSet(LearnedKey, null) ?? []);
    }

    /// <summary>Gives a group the owner's name; a blank name takes it away.</summary>
    public static void Name(Context context, string threadKey, string? name)
    {
        var names = Load(context).Name(threadKey, name);
        Prefs(context).Edit()!.PutStringSet(NamedKey, names.NamedStored().ToList())!.Apply();
    }

    /// <summary>Records what Google Messages calls a group; a write only when it changed.</summary>
    public static void Learn(Context context, string threadKey, string? title)
    {
        var known = Load(context);
        var learned = known.Learn(threadKey, title);
        if (!ReferenceEquals(known, learned))
        {
            Prefs(context).Edit()!.PutStringSet(LearnedKey, learned.LearnedStored().ToList())!.Apply();
        }
    }

    private static ISharedPreferences Prefs(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
