using Android.Content;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// The on-device home of the empty-Mailbox pool's state. The decision of WHICH line to
/// show lives in <see cref="EmptyMailbox"/>, which is pure; this only supplies the roll,
/// the clock and somewhere to keep what came back.
/// </summary>
public sealed class EmptyMailboxStore
{
    private const string PreferencesName = "mailboxVoice";
    private const string StateKey = "state";

    private readonly ISharedPreferences preferences;

    public EmptyMailboxStore(Context context) =>
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    /// <summary>
    /// Draws the next line and records the climb. <paramref name="mailArrivedSinceLastView"/>
    /// is what separates the compulsive check from a genuinely cleared desk.
    /// </summary>
    public string Draw(bool mailArrivedSinceLastView)
    {
        var state = EmptyMailboxCodec.Decode(preferences.GetString(StateKey, null));
        var draw = EmptyMailbox.Draw(
            state, mailArrivedSinceLastView, DateTimeOffset.UtcNow, Random.Shared.Next());

        preferences.Edit()!.PutString(StateKey, EmptyMailboxCodec.Encode(draw.Next))!.Apply();
        return draw.Line;
    }
}
