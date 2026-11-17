using System.Globalization;
using Foreword.Domain.Models;

namespace Foreword.Domain.Voice;

/// <summary>
/// What the morning's three printed lines are told (audit W10.4): the brief's opening, the day's narrator and the
/// checklist's coach, each a seat on the suite's voice-line Call Sheet. Counts in words and one time, nothing else —
/// never an event's title, guest or place (the calendar is the household's), and nothing from the prayer page, which
/// these take no type that could carry. An empty list has nothing to say, so its line hides.
/// </summary>
public static class MorningVoiceFacts
{
    public static IReadOnlyList<string> Brief(int stories, bool crossword)
    {
        var facts = new List<string>
        {
            $"{Sentence(Count(stories))} {(stories == 1 ? "story" : "stories")} in the brief this morning.",
        };
        if (crossword)
        {
            facts.Add("The crossword is at the back.");
        }
        return facts;
    }

    public static IReadOnlyList<string> Day(DayAgenda agenda)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        var timed = agenda.Events.Where(item => !item.IsAllDay).OrderBy(item => item.Start).ToList();
        var allDay = agenda.Events.Count(item => item.IsAllDay);
        if (timed.Count == 0 && allDay == 0)
        {
            return ["Nothing is on the calendar today."];
        }

        var counted = timed.Count == 0
            ? $"Nothing is timed on the calendar today, and {Count(allDay)} all-day {(allDay == 1 ? "entry" : "entries")}."
            : $"{Sentence(Count(timed.Count))} {(timed.Count == 1 ? "thing is" : "things are")} on the calendar today"
              + (allDay == 0 ? "." : $", and {Count(allDay)} all-day {(allDay == 1 ? "entry" : "entries")}.");
        var facts = new List<string> { counted };
        if (timed.Count > 0)
        {
            facts.Add($"The first begins at {Clock(timed[0].Start)}.");
        }
        return facts;
    }

    public static IReadOnlyList<string> Checklist(int tasks) =>
        tasks <= 0 ? [] : [$"{Sentence(Count(tasks))} {(tasks == 1 ? "task is" : "tasks are")} on today's list."];

    private static readonly string[] Words =
        ["no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    private static string Count(int count) =>
        count >= 0 && count < Words.Length ? Words[count] : count.ToString(CultureInfo.InvariantCulture);

    private static string Sentence(string text) => string.Concat(char.ToUpperInvariant(text[0]).ToString(), text[1..]);

    private static string Clock(DateTime time) =>
        $"{(time.Hour % 12 == 0 ? 12 : time.Hour % 12)}:{time.Minute:00} {(time.Hour < 12 ? "am" : "pm")}";
}
