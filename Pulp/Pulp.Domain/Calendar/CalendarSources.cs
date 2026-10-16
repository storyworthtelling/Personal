using Pulp.Domain.Core;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Calendar;

/// <summary>
/// A new episode of a show on someone's Continue Watching (D146). <paramref name="Watchers"/> are the
/// adults whose list it is on. <paramref name="LivePremiere"/> is set only when the episode airs live
/// at a known time; a streaming drop has a day, not a time (D148).
/// </summary>
public sealed record EpisodeRelease(
    TitleId Show,
    string ShowName,
    int Season,
    int Episode,
    DateOnly Date,
    DateTimeOffset? LivePremiere,
    ServiceId Service,
    IReadOnlyList<ViewerId> Watchers);

/// <summary>A playlist a title sits on: whose it is and whether it is shared (D244).</summary>
public sealed record PlaylistShelf(string Name, ViewerId Owner, bool Shared);

/// <summary>The release of a title that sits on one or more playlists (D146).</summary>
public sealed record TitleRelease(
    TitleId Title,
    string Name,
    TitleKind Kind,
    DateOnly Date,
    DateTimeOffset? LivePremiere,
    ServiceId? Service,
    IReadOnlyList<PlaylistShelf> Playlists);

/// <summary>A "plan to watch" on a date (D68), for the viewers it names (D73).</summary>
public sealed record PlannedViewing(
    string PlanId,
    TitleId? Title,
    string Name,
    DateOnly Date,
    TimeOnly? Time,
    IReadOnlyList<ViewerId> Viewers,
    string? Place = null);

/// <summary>
/// Everything the calendar is built from. There is deliberately no leaving-soon source: D146 keeps
/// leaving-soon dates off the calendar, on the row only.
/// </summary>
public sealed record CalendarSources(
    IReadOnlyList<EpisodeRelease> Episodes,
    IReadOnlyList<TitleRelease> Releases,
    IReadOnlyList<Fixture> Games,
    IReadOnlyList<PlannedViewing> Plans);
