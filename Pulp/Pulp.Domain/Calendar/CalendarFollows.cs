using Pulp.Domain.Core;
using Pulp.Domain.People;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Calendar;

/// <summary>
/// Which team follows the household calendar may read. Spoiler holds are built for adults only, and the
/// calendar is shared, so a follow by a child, by a viewer no longer on the roster, or with no roster at all
/// is left out: it would put a child's name on the shared calendar, or make
/// <see cref="CalendarItems.ForHousehold"/> throw for a follower it holds nothing for.
/// </summary>
public static class CalendarFollows
{
    public static IReadOnlyList<SportsFollow> OfAdults(IEnumerable<SportsFollow> follows, HouseholdRoster? roster)
    {
        ArgumentNullException.ThrowIfNull(follows);
        if (roster is null)
        {
            return [];
        }

        return follows.Where(follow => roster.Contains(follow.Viewer) && !roster.IsChild(follow.Viewer)).ToList();
    }

    /// <summary>Whether a follow for <paramref name="viewer"/> is refused: a child on the roster. Following is an adult's act (D60).</summary>
    public static bool RefusesFollow(HouseholdRoster? roster, ViewerId viewer) =>
        roster is not null && roster.Contains(viewer) && roster.IsChild(viewer);
}
