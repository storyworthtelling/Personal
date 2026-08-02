using Android.Content;
using FocusPhone.Domain.Music;
using FocusPhone.Infrastructure.Music;

namespace FocusPhone.Audio;

/// <summary>
/// The on-device home of the raw listening history — SharedPreferences JSON via the unit-tested
/// <see cref="ListeningEventCodec"/>, capped to the most recent sessions by <see cref="ListeningHistory.Trim"/>.
/// The raw per-play history never leaves the phone by design; only the summarized Taste Profile syncs to
/// Drive. <see cref="Clear"/> backs the kill switch — turning learning off wipes what was gathered.
/// </summary>
public class ListeningEventStore
{
    private const string PreferencesName = "listeningHistory";
    private const string EventsKey = "events";
    private const int MaxSessions = 200;

    private readonly ISharedPreferences preferences;

    public ListeningEventStore(Context context) =>
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public IReadOnlyList<ListeningEvent> Load() =>
        ListeningEventCodec.Decode(preferences.GetString(EventsKey, null));

    public void Append(IReadOnlyList<ListeningEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        var combined = new List<ListeningEvent>(Load());
        combined.AddRange(events);
        var trimmed = ListeningHistory.Trim(combined, MaxSessions);
        preferences.Edit()!.PutString(EventsKey, ListeningEventCodec.Encode(trimmed))!.Apply();
    }

    public void Clear() => preferences.Edit()!.Remove(EventsKey)!.Apply();
}
