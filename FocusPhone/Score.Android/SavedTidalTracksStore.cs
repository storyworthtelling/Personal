using Android.Content;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;

namespace Score;

/// <summary>
/// Persists the TIDAL tracks the owner has explicitly saved — the "streamed" half
/// <see cref="MusicLibrary.Streamed"/> feeds into <c>LibraryMerge.Merge</c>, so a
/// saved track shows up in Songs, playlists, shuffle, and smart mix exactly like a
/// Drive-scanned one, per the merged-library rule. Only a track someone tapped Save
/// on ever lands here — a search result is never added just for having been found or
/// played (Discover's own Play is an audition, and touches nothing).
///
/// Same SharedPreferences-plus-codec shape as <see cref="SubscriptionStore"/>: no
/// database needed for a list this small, and Add/Remove read-modify-write the whole
/// list rather than something more granular, exactly like that store's
/// <c>SubscriptionList</c>.
/// </summary>
public sealed class SavedTidalTracksStore(Context context)
{
    private const string PreferencesName = "savedTidalTracks";
    private const string TracksKey = "tracks";
    private const string LastValidatedKey = "lastValidatedUtcTicks";

    private ISharedPreferences Prefs => context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public IReadOnlyList<Track> Load() => SavedTidalTracksCodec.Decode(Prefs.GetString(TracksKey, null));

    private void Save(IReadOnlyList<Track> tracks) =>
        Prefs.Edit()!.PutString(TracksKey, SavedTidalTracksCodec.Encode(tracks))!.Apply();

    public bool Contains(string trackId) => Load().Any(track => track.Id == trackId);

    /// <summary>No-ops if the track (by id) is already saved, so tapping Save twice
    /// on the same result can never duplicate it in the catalog.</summary>
    public void Add(Track track)
    {
        var tracks = Load();
        if (tracks.Any(existing => existing.Id == track.Id))
        {
            return;
        }

        Save([.. tracks, track]);
    }

    public void Remove(string trackId) => Save([.. Load().Where(track => track.Id != trackId)]);

    /// <summary>Replaces a saved track's record in place (by id) — how
    /// <see cref="MusicLibrary.RevalidateSavedTidalTracksAsync"/> updates a track's
    /// <c>Availability</c> after re-checking it, without disturbing its position or
    /// any other saved track.</summary>
    public void Replace(Track track) =>
        Save([.. Load().Select(existing => existing.Id == track.Id ? track : existing)]);

    /// <summary>When the saved list was last checked against TIDAL's catalogue, or
    /// null if never. Read/written as raw UTC ticks — SharedPreferences has no native
    /// DateTimeOffset type, and ticks round-trip exactly without a format to agree on.</summary>
    public DateTimeOffset? LastValidatedUtc
    {
        get
        {
            var ticks = Prefs.GetLong(LastValidatedKey, -1);
            return ticks < 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
        set => Prefs.Edit()!.PutLong(LastValidatedKey, value?.UtcTicks ?? -1)!.Apply();
    }
}
