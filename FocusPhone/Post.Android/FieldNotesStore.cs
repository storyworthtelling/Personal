using Android.Content;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// Where field notes accumulate between launches. They are gathered across a whole
/// test-plan run — days of it, across the UI process and the sync worker alike — so losing
/// them on a process death would leave the owner's phone time producing nothing.
///
/// Read-modify-write, because the two processes both observe. The window for a lost update
/// is small and the cost of one is a count off by one, which is the right trade for
/// observations: a lock here would be machinery guarding a tally.
/// </summary>
internal static class FieldNotesStore
{
    private const string PreferencesName = "fieldNotes";
    private const string NotesKey = "notes";

    public static FieldNotes Load(Context context) =>
        FieldNotesCodec.Decode(Prefs(context).GetString(NotesKey, null));

    /// <summary>Applies an observation. Never throws — a note is never worth a failure.</summary>
    public static void Record(Context context, Func<FieldNotes, FieldNotes> observe)
    {
        try
        {
            var updated = observe(Load(context));
            Prefs(context).Edit()!.PutString(NotesKey, FieldNotesCodec.Encode(updated))!.Apply();
        }
        catch (Exception e)
        {
            Android.Util.Log.Info("Post", $"Could not keep a field note: {e.Message}");
        }
    }

    /// <summary>
    /// True the first time it is asked on a given day, so the periodic observations (archive size, attachments,
    /// contacts without a photo) are taken once a day rather than on every 30-minute sync (efficiency audit §1).
    /// </summary>
    public static bool FirstTimeToday(Context context, DateOnly today)
    {
        var stamp = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var prefs = Prefs(context);
        if (prefs.GetString(ObservedOnKey, null) == stamp)
        {
            return false;
        }
        prefs.Edit()!.PutString(ObservedOnKey, stamp)!.Apply();
        return true;
    }

    private const string ObservedOnKey = "observedOn";

    private static ISharedPreferences Prefs(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
