using Constitution.Domain.Documents;
using Constitution.Domain.Workouts;

namespace Constitution.Tests.DigestPages;

/// <summary>C-2.6.1 — done against planned as a count and minutes; a mark per session day; rest days unmarked; no streak or percentage.</summary>
public class DigestWorkoutsTests
{
    [Fact]
    public void CountsAndMinutes()
    {
        var summary = DigestWorkouts.Summarise(DigestFixture.Week());
        Assert.Equal(3, summary.Done);
        Assert.Equal(4, summary.Planned);
        Assert.Equal(105, summary.DoneMinutes);
        Assert.Equal(125, summary.PlannedMinutes);
        Assert.Equal("3 of 4 workouts done · 105 of 125 minutes", summary.Headline);
    }

    [Fact]
    public void RestDaysAreUnmarked()
    {
        var summary = DigestWorkouts.Summarise(DigestFixture.Week());
        Assert.Equal(7, summary.Days.Count);
        Assert.Equal(DigestFixture.WeekStart, summary.Days[0].Day);
        var marks = summary.Days.Select(day => day.Mark).ToList();
        Assert.Equal(
            [DigestMark.None, DigestMark.Filled, DigestMark.Filled, DigestMark.None, DigestMark.Open, DigestMark.Filled, DigestMark.None],
            marks);
    }

    [Fact]
    public void AnUnreadDayIsOpenAndNotDone()
    {
        var week = DigestFixture.Week() with
        {
            Sessions = [new DigestSession(new DateOnly(2026, 3, 9), "Strength", 30, SessionOutcome.Unsettled, null)],
        };
        var summary = DigestWorkouts.Summarise(week);
        Assert.Equal(0, summary.Done);
        Assert.Equal(1, summary.Planned);
        Assert.Equal(DigestMark.Open, summary.Days[1].Mark);
    }

    [Fact]
    public void CarriesNoStreakOrPercentage()
    {
        var page = DigestWorkouts.Page(DigestWorkouts.Summarise(DigestFixture.Week()), DigestFixture.Saturday, 1, 5);
        var text = string.Join(" ", page.Printed.Select(item => item.Text)).ToLowerInvariant();
        Assert.Contains("3 of 4 workouts done", text);
        Assert.DoesNotContain("%", text);
        Assert.DoesNotContain("streak", text);
        Assert.DoesNotContain("best", text);
        Assert.DoesNotContain("record", text);
        Assert.DoesNotContain("than last", text);
    }

    [Fact]
    public void OneSquarePerSessionDay()
    {
        var page = DigestWorkouts.Page(DigestWorkouts.Summarise(DigestFixture.Week()), DigestFixture.Saturday, 1, 5);
        Assert.Equal(3, page.Printed.Count(item => item.Name.StartsWith("mark-filled-", StringComparison.Ordinal)));
        Assert.Equal(1, page.Printed.Count(item => item.Name.StartsWith("mark-open-", StringComparison.Ordinal)));
    }

    [Fact]
    public void WhatMovedIsInSentences()
    {
        var page = DigestWorkouts.Page(DigestWorkouts.Summarise(DigestFixture.Week()), DigestFixture.Saturday, 1, 5);
        var moved = string.Join(" ", page.Printed.Where(item => item.Name.StartsWith("moved-", StringComparison.Ordinal)).Select(item => item.Text));
        Assert.Equal("Wednesday's walk moved to Thursday for the forecast.", moved);
    }

    [Fact]
    public void RatingsAreSummarisedOnTheDay()
    {
        var page = DigestWorkouts.Page(DigestWorkouts.Summarise(DigestFixture.Week()), DigestFixture.Saturday, 1, 5);
        Assert.Contains(page.Printed, item => item.Text is not null && item.Text.Contains("Strength") && item.Text.Contains("right"));
    }

    [Fact]
    public void ThePageKeepsTheNotesMargin()
    {
        var page = DigestWorkouts.Page(DigestWorkouts.Summarise(DigestFixture.Week()), DigestFixture.Saturday, 1, 5);
        Assert.Contains(page.Map.Targets, target => target.Id == WorkoutIds.Notes);
    }
}
