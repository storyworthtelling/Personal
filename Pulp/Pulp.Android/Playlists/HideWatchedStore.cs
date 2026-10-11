using Android.Content;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;

namespace Pulp.Playlists;

/// <summary>
/// "Hide watched", remembered per playlist on this phone only (D292) — install-local preferences, so
/// the other adult's page is never changed by it. The file name and key are frozen in
/// <see cref="HideWatchedPreference"/>; an absent key is off, as the chip starts.
/// </summary>
internal sealed class HideWatchedStore
{
    private readonly ISharedPreferences preferences;

    public HideWatchedStore(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        preferences = context.GetSharedPreferences(HideWatchedPreference.FileName, FileCreationMode.Private)
            ?? throw new InvalidOperationException("Pulp could not open its hide-watched preferences.");
    }

    public bool IsOn(PlaylistId playlist) => preferences.GetBoolean(HideWatchedPreference.Key(playlist), false);

    public void Set(PlaylistId playlist, bool on) =>
        preferences.Edit()!.PutBoolean(HideWatchedPreference.Key(playlist), on)!.Apply();
}
