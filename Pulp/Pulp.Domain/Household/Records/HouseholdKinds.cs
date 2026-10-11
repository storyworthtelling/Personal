namespace Pulp.Domain.Household.Records;

/// <summary>
/// The kind half of every household record's key. <b>Frozen once shipped</b>: the file outlives every
/// build that reads it, so a renamed kind is a reader that finds nothing while the old records sit
/// unread in the file for good. <c>Pulp.Tests/Guard/HouseholdKindsTests</c> holds each value, and a
/// new kind has to be added there to ship.
/// </summary>
public static class HouseholdKinds
{
    /// <summary>One viewer's one sitting with a title (<see cref="WatchedSitting"/>).</summary>
    public const string Watched = "watched";

    /// <summary>A named, hand-ordered list with its owner and sharing (<see cref="HouseholdPlaylist"/>).</summary>
    public const string Playlist = "playlist";

    /// <summary>A viewer's "Not for me" (<see cref="ViewerVeto"/>); "Bring it back" is its tombstone.</summary>
    public const string Veto = "veto";

    /// <summary>A child's own star on a title (<see cref="ChildStarEntry"/>).</summary>
    public const string ChildStar = "child-star";

    /// <summary>A parent's pick for a child (<see cref="ParentPickEntry"/>), which allow-lists its title.</summary>
    public const string ParentPick = "parent-pick";

    /// <summary>"Open there instead" — a title's chosen home (<see cref="HomeChoice"/>).</summary>
    public const string HomeOverride = "home-override";

    /// <summary>One child's filter: the ceiling, the allow-list and the blocks.</summary>
    public const string ChildRules = "child-rules";

    /// <summary>One person on the household roster, and whether they are a child.</summary>
    public const string Viewer = "viewer";

    /// <summary>A service the owner attested keeps adult profiles PIN-locked (D210).</summary>
    public const string AttestedService = "attested-service";

    /// <summary>A service the household has.</summary>
    public const string HouseholdService = "household-service";

    /// <summary>
    /// A viewer's "Remove from Continue watching" (<see cref="ContinueDismissedEntry"/>); undo is its
    /// tombstone.
    /// </summary>
    public const string ContinueDismissed = "continue-dismissed";

    /// <summary>A service's plan as Settings records it: its tier, its picture and whether Flyleaf shows its app (<see cref="ServicePlanEntry"/>).</summary>
    public const string ServicePlan = "service-plan";

    /// <summary>The name a viewer goes by (<see cref="ViewerNameEntry"/>); the id stays, so a rename moves no record.</summary>
    public const string ViewerName = "viewer-name";

    /// <summary>The id of the household's Pulp Google calendar (<see cref="PulpCalendarEntry"/>), written by the owner's install.</summary>
    public const string PulpCalendar = "pulp-calendar";

    /// <summary>One adult following one team (<see cref="Pulp.Domain.Sports.SportsFollow"/>, D60); unfollowing is its tombstone.</summary>
    public const string SportsFollow = "sports-follow";
}
