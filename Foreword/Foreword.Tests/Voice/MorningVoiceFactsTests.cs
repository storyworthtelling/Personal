using Xunit;
using Foreword.Domain.Models;
using Foreword.Domain.Voice;

namespace Foreword.Tests.Voice;

/// <summary>
/// What the morning's three printed lines are told (audit W10.4): counts in words and a time, never a title, a guest,
/// a place or anything from the prayer page. An empty checklist has nothing to say, so its line hides.
/// </summary>
public class MorningVoiceFactsTests
{
    private static readonly DateOnly Day = new(2026, 3, 9);

    private static CalendarEvent At(int hour, int minute, string title, bool allDay = false) => new()
    {
        Title = title,
        Start = Day.ToDateTime(new TimeOnly(hour, minute)),
        End = Day.ToDateTime(new TimeOnly(hour, minute)).AddHours(1),
        IsAllDay = allDay,
        Location = "12 Example Street",
    };

    [Fact]
    public void TheBriefIsToldHowManyStoriesAndWhetherThereIsACrossword()
    {
        Assert.Equal(["Eight stories in the brief this morning.", "The crossword is at the back."],
            MorningVoiceFacts.Brief(stories: 8, crossword: true));
        Assert.Equal(["One story in the brief this morning."], MorningVoiceFacts.Brief(stories: 1, crossword: false));
    }

    [Fact]
    public void TheDayIsToldHowManyThingsAndWhenTheFirstBegins()
    {
        var agenda = new DayAgenda
        {
            Date = Day,
            Events = [At(14, 0, "Dentist with Sam"), At(8, 30, "School run"), At(0, 0, "Robin’s birthday", allDay: true)],
        };

        var facts = MorningVoiceFacts.Day(agenda);

        Assert.Equal(["Two things are on the calendar today, and one all-day entry.", "The first begins at 8:30 am."], facts);
    }

    [Fact]
    public void ADayWithNothingOnItSaysSo()
    {
        Assert.Equal(["Nothing is on the calendar today."],
            MorningVoiceFacts.Day(new DayAgenda { Date = Day, Events = [] }));
    }

    [Fact]
    public void NoTitleGuestOrPlaceReachesTheVoice()
    {
        var agenda = new DayAgenda { Date = Day, Events = [At(9, 0, "Dentist with Sam")] };

        var said = string.Join(" ", MorningVoiceFacts.Day(agenda));

        Assert.DoesNotContain("Dentist", said, StringComparison.Ordinal);
        Assert.DoesNotContain("Sam", said, StringComparison.Ordinal);
        Assert.DoesNotContain("Example", said, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChecklistIsToldHowManyTasksAndHidesWithNone()
    {
        Assert.Equal(["Three tasks are on today’s list."], MorningVoiceFacts.Checklist(tasks: 3));
        Assert.Equal(["One task is on today’s list."], MorningVoiceFacts.Checklist(tasks: 1));
        Assert.Empty(MorningVoiceFacts.Checklist(tasks: 0));
    }
}
