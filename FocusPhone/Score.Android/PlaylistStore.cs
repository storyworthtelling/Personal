using Android.Content;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;

namespace Score;

/// <summary>
/// Persists the owner's playlists. A thin <see cref="ISharedPreferences"/> adapter
/// over <see cref="PlaylistCodec"/>, in a file of its own rather than beside the
/// music settings: a corrupt playlist blob should not be able to take the chosen
/// folder and the Google account down with it.
///
/// Local for now, like the queues. Unlike the queues they are <b>not</b>
/// re-derivable — a hand-made list exists nowhere else — so they are a candidate
/// for the Drive backup the audio library already has, once there is something to
/// lose.
/// </summary>
public class PlaylistStore
{
    private const string PreferencesName = "playlists";
    private const string PlaylistsKey = "playlists";

    private readonly ISharedPreferences preferences;

    public PlaylistStore(Context context) =>
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public IReadOnlyList<Playlist> Load() =>
        PlaylistCodec.Decode(preferences.GetString(PlaylistsKey, null));

    public void Save(IReadOnlyList<Playlist> playlists) =>
        preferences.Edit()!.PutString(PlaylistsKey, PlaylistCodec.Encode(playlists))!.Apply();
}
