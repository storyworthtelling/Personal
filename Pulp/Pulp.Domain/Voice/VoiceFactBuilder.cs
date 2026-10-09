using Pulp.Domain.Core;

namespace Pulp.Domain.Voice;

/// <summary>
/// The facts each adult seat may be told, reduced to words before they leave. What is never passed,
/// by these signatures rather than by care: another viewer's history (D41, D195), a child's history
/// (D23), more than one past title, or any figure in digits. A child's seats have their own builder,
/// <see cref="ChildVoiceFacts"/>, so none of these can reach one.
/// </summary>
public static class VoiceFactBuilder
{
    /// <summary>
    /// The Continue Watching greeting (D118): the part of the day, whether this is the first open
    /// today (Score's context), and the viewer's own counts (D97 — their Continue Watching is private
    /// to them, and this greeting is theirs).
    /// </summary>
    public static VoiceFacts Greeting(DateTimeOffset localNow, bool firstOpenOfDay, int inProgress, int arrivedFriday)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inProgress);
        ArgumentOutOfRangeException.ThrowIfNegative(arrivedFriday);

        var lines = new List<string> { $"It is {PartOfDay(localNow)}." };
        if (firstOpenOfDay)
        {
            lines.Add("This is the first time Pulp has been opened today.");
        }

        lines.Add(inProgress switch
        {
            0 => "Nothing is in progress.",
            1 => "There is one title in progress.",
            _ => $"There are {NumberWords.Of(inProgress)} titles in progress.",
        });
        AddArrivedFriday(lines, arrivedFriday);
        return new VoiceFacts(VoiceSeat.Greeting, lines, null);
    }

    /// <summary>
    /// The why-this line (D36, D41): the title, and — for the device owner only — the ONE past title
    /// of their own they finished most recently. Another viewer's finished titles are discarded
    /// whoever passed them in; the other adult hears no past title at all; a child has no taste
    /// profile (D23), so a child's title page has no why-this and the line is hidden.
    /// </summary>
    public static VoiceFacts WhyThis(ViewerId viewer, ViewerStanding standing, string titleName, IEnumerable<PastTitle> finishedAlike)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titleName);
        ArgumentNullException.ThrowIfNull(finishedAlike);

        if (standing == ViewerStanding.Child)
        {
            return new VoiceFacts(VoiceSeat.WhyThis, [], null);
        }

        var lines = new List<string>
        {
            $"The title is {titleName}.",
            "It was chosen from the owner's own taste.",
        };

        if (standing == ViewerStanding.DeviceOwner)
        {
            var past = finishedAlike
                .Where(title => title.Viewer.Equals(viewer) && !string.IsNullOrWhiteSpace(title.Name))
                .OrderByDescending(title => title.FinishedAt)
                .FirstOrDefault();
            if (past is not null)
            {
                lines.Add($"The viewer recently finished {past.Name}, which is like it.");
            }
        }

        return new VoiceFacts(VoiceSeat.WhyThis, lines, null);
    }

    /// <summary>A line per playlist (D38): its name and how much of it has been watched.</summary>
    public static VoiceFacts Playlist(string name, int entries, int watched)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(entries);
        ArgumentOutOfRangeException.ThrowIfNegative(watched);

        var held = entries == 1 ? "one title" : $"{NumberWords.Of(entries)} titles";
        return new VoiceFacts(VoiceSeat.PlaylistLine,
        [
            $"The playlist is called {name}.",
            $"It holds {held}, and {NumberWords.Of(Math.Min(watched, entries))} of them have been watched.",
        ], null);
    }

    /// <summary>An empty home (mockup 13): nothing started, and what Friday's stack brought.</summary>
    public static VoiceFacts EmptyContinueWatching(int arrivedFriday)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(arrivedFriday);
        var lines = new List<string> { "Nothing has been started yet." };
        AddArrivedFriday(lines, arrivedFriday);
        return new VoiceFacts(VoiceSeat.EmptyContinueWatching, lines, null);
    }

    public static VoiceFacts EmptyPlaylists() =>
        new(VoiceSeat.EmptyPlaylists, ["No playlists have been made yet.", "One can be made from a prompt."], null);

    public static VoiceFacts EmptyPlaylist(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new VoiceFacts(VoiceSeat.EmptyPlaylist, [$"The playlist {name} has nothing on it yet."], null);
    }

    public static VoiceFacts EmptyRecentlyWatched() =>
        new(VoiceSeat.EmptyRecentlyWatched, ["Nothing has been watched yet."], null);

    public static VoiceFacts EmptyCalendar(int daysAhead)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(daysAhead);
        var span = daysAhead % 7 == 0
            ? (daysAhead == 7 ? "week" : $"{NumberWords.Of(daysAhead / 7)} weeks")
            : (daysAhead == 1 ? "day" : $"{NumberWords.Of(daysAhead)} days");
        return new VoiceFacts(VoiceSeat.EmptyCalendar, [$"Nothing is on the household calendar in the next {span}."], null);
    }

    public static VoiceFacts EmptySports() =>
        new(VoiceSeat.EmptySports, ["No team is followed yet."], null);

    /// <summary>A used-up stack (D112): no refill, and how long until Friday's turnover (D110).</summary>
    public static VoiceFacts StackUsedUp(DateOnly today)
    {
        var days = ((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7;
        if (days == 0)
        {
            days = 7;
        }

        var when = days == 1 ? "tomorrow" : $"in {NumberWords.Of(days)} days";
        return new VoiceFacts(VoiceSeat.StackUsedUp,
            ["Everything in this week's stack has been seen.", $"The next stack arrives on Friday, {when}."], null);
    }

    internal static string PartOfDay(DateTimeOffset localNow) => localNow.Hour switch
    {
        < 5 => "late at night",
        < 12 => "morning",
        < 17 => "afternoon",
        < 22 => "evening",
        _ => "late at night",
    };

    private static void AddArrivedFriday(List<string> lines, int arrivedFriday)
    {
        if (arrivedFriday > 0)
        {
            lines.Add(arrivedFriday == 1
                ? "Friday's stack brought one new title."
                : $"Friday's stack brought {NumberWords.Of(arrivedFriday)} new titles.");
        }
    }
}
