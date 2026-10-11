using Pulp.Domain.Core;

namespace Pulp.Domain.Calendar;

/// <summary>How a <see cref="CalendarItem"/> reads as an event: the title says whose (D147).</summary>
public static class CalendarDrafts
{
    public static readonly TimeSpan GameLength = TimeSpan.FromHours(3);

    public static readonly TimeSpan TimedLength = TimeSpan.FromHours(1);

    public static CalendarEventDraft From(CalendarItem item, Func<ViewerId, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(nameOf);

        var label = item.Whose.Count == 0 ? string.Empty : " (" + string.Join(", ", item.Whose.Select(nameOf)) + ")";
        var title = item.Kind switch
        {
            CalendarItemKind.NewEpisode => "New episode — " + item.Name + label,
            CalendarItemKind.Release => item.Name + " arrives" + label,
            CalendarItemKind.Plan => "Plan — " + item.Name + label,
            _ => item.Name + label,
        };

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Detail))
        {
            lines.Add(item.Detail);
        }

        if (item.Playlists.Count > 0)
        {
            lines.Add("On: " + string.Join(", ", item.Playlists));
        }

        return new CalendarEventDraft(
            item.Key,
            title,
            lines.Count == 0 ? null : string.Join('\n', lines),
            item.When,
            item.Kind == CalendarItemKind.Game ? GameLength : TimedLength);
    }
}
