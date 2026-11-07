using Android.Content;

namespace WatchSpike;

/// <summary>
/// The spike's only memory: a few lines in its own preferences, shown on its screen so the owner
/// can read them back. It never holds a message's words, and keeps only a number's last two digits
/// for display (the full number is kept only so the "reply" test has somewhere to send).
/// </summary>
public static class SpikeLog
{
    private const string PrefsName = "watchspike";

    public static void TextArrived(Context context, string address)
    {
        var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
        var count = prefs.GetInt("texts", 0) + 1;
        var tail = address.Length >= 2 ? address[^2..] : "??";
        var editor = prefs.Edit()!;
        editor.PutInt("texts", count);
        editor.PutString("lastSender", address);
        editor.PutString("lastText", $"Text {count} arrived {DateTime.Now:h:mm tt} from a number ending {tail}.");
        editor.Apply();
    }

    public static void Note(Context context, string line)
    {
        var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
        var editor = prefs.Edit()!;
        editor.PutString("note", $"{DateTime.Now:h:mm tt} {line}");
        editor.Apply();
    }

    public static int Texts(Context context) => context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.GetInt("texts", 0);

    public static string? LastText(Context context) => context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.GetString("lastText", null);

    public static string? LastSender(Context context) => context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.GetString("lastSender", null);

    public static string? LastNote(Context context) => context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!.GetString("note", null);
}
