using Android.Content;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// The senders the owner has corrected Post about. Promoting a sender is permanent, so
/// this outlives everything else about a message — including the message.
/// </summary>
internal static class SenderPromotionStore
{
    private const string PreferencesName = "senderPromotions";
    private const string PromotedKey = "promoted";

    public static SenderPromotions Load(Context context)
    {
        var stored = Prefs(context).GetStringSet(PromotedKey, null);
        return SenderPromotions.From(stored ?? []);
    }

    public static void Save(Context context, SenderPromotions promotions) =>
        Prefs(context).Edit()!
            .PutStringSet(PromotedKey, promotions.All.ToList())!
            .Apply();

    private static ISharedPreferences Prefs(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
}
