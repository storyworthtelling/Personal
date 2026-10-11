using System.Globalization;
using Pulp.Domain.Household.Records;

namespace Pulp.Domain.Library;

/// <summary>
/// Where "Hide watched" is remembered (D292): per playlist, on this phone only — never in the shared
/// household file, so one adult hiding what they have seen does not change the other's page. The file
/// name and the key prefix are frozen (<c>PulpInvariantsTests</c>): an upgraded phone finds the owner's
/// settings by them.
/// </summary>
public static class HideWatchedPreference
{
    /// <summary>The preferences file, named once (never derived from a member name).</summary>
    public const string FileName = "pulp-playlist-hide-watched";

    public const string KeyPrefix = "hide-watched:";

    /// <summary>
    /// The playlist's key. A <see cref="PlaylistId"/> compares without case, so the key folds case too —
    /// one playlist, one setting, however its id is spelled.
    /// </summary>
    public static string Key(PlaylistId playlist) =>
        string.IsNullOrWhiteSpace(playlist.Value)
            ? throw new ArgumentException("An unset playlist id has no remembered setting.", nameof(playlist))
            : KeyPrefix + playlist.Value.ToLowerInvariant();
}
