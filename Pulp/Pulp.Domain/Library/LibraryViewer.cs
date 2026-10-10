using Pulp.Domain.Core;
using Pulp.Domain.People;

namespace Pulp.Domain.Library;

/// <summary>Whether the phone knows whose private lists to show.</summary>
public enum LibraryViewerStatus
{
    /// <summary>Nobody is on the roster yet (first run is Release D's), so there is nothing to show.</summary>
    NoRoster,

    /// <summary>A roster exists but does not say which adult holds this phone.</summary>
    Unmatched,

    Known,
}

public sealed record LibraryViewerAnswer(LibraryViewerStatus Status, ViewerId? Viewer);

/// <summary>
/// Whose Recently watched and whose personal playlists this phone shows (D97). Nothing yet records
/// which household viewer holds this phone — that belongs to Release D's Household group — so the
/// only answer that cannot be wrong is the roster's one adult. With two adults it refuses rather than
/// picks: a wrong pick shows one adult the other's private history, which is worse than an honest
/// sentence. A child is never the phone's viewer here; a child's page is Favorites (D213).
/// </summary>
public static class LibraryViewer
{
    /// <summary>The status sentence for <see cref="LibraryViewerStatus.Unmatched"/>. Never voiced.</summary>
    public const string UnmatchedStatus = "This phone isn't matched to anyone in the household yet.";

    public static LibraryViewerAnswer Me(HouseholdRoster? roster)
    {
        if (roster is null)
        {
            return new LibraryViewerAnswer(LibraryViewerStatus.NoRoster, null);
        }

        var adults = roster.Viewers.Where(viewer => !viewer.IsChild).ToList();
        return adults.Count == 1
            ? new LibraryViewerAnswer(LibraryViewerStatus.Known, adults[0].Id)
            : new LibraryViewerAnswer(LibraryViewerStatus.Unmatched, null);
    }
}
