using Android.Content;

namespace Post;

/// <summary>
/// The newest message Post has alerted for, per Google Messages conversation, so a
/// notification Messages re-posts (a second message, a reboot, a read elsewhere) does not
/// buzz again for what was already announced. On this phone only; losing it costs at most
/// one repeat buzz inside <see cref="Codex.Domain.Post.TextAlerts.Fresh"/>.
/// </summary>
internal static class TextAlertMemory
{
    private const string PreferencesName = "textAlerts";

    public static DateTimeOffset? HeardThrough(Context context, string conversation)
    {
        var stored = Preferences(context).GetLong(conversation, -1);
        return stored < 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(stored);
    }

    public static void Remember(Context context, string conversation, DateTimeOffset heardThrough) =>
        Preferences(context).Edit()!.PutLong(conversation, heardThrough.ToUnixTimeMilliseconds())!.Apply();

    private static ISharedPreferences Preferences(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
