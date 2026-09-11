using Foreword.Domain.Models;
using Foreword.Domain.Tasks;
using Xunit;

namespace Foreword.Tests.Tasks;

/// <summary>
/// Which of the checklist's three time bands a task belongs in, inferred rather than set by hand
/// (Q63). Nobody is going to tag every task with a time of day, and a list that all lands in one
/// band is the single column the three-band layout exists to avoid.
/// </summary>
public class TaskBandsTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);

    private static TodoItem Task(string title, DateOnly? due = null) =>
        new() { Title = title, Due = due };

    [Fact]
    public void MostThingsHappenDuringTheDay()
    {
        Assert.Equal(TaskBand.DuringTheDay, TaskBands.For(Task("Call Mom")));
        Assert.Equal(TaskBand.DuringTheDay, TaskBands.For(Task("Order Wren's cleats")));
    }

    [Fact]
    public void SomeThingsAreObviouslyMorning()
    {
        Assert.Equal(TaskBand.Morning, TaskBands.For(Task("Vitamins")));
        Assert.Equal(TaskBand.Morning, TaskBands.For(Task("Read the brief")));
        Assert.Equal(TaskBand.Morning, TaskBands.For(Task("Breakfast with Dana")));
    }

    [Fact]
    public void SomeThingsAreObviouslyEvening()
    {
        Assert.Equal(TaskBand.Evening, TaskBands.For(Task("Bins out")));
        Assert.Equal(TaskBand.Evening, TaskBands.For(Task("Sign Wren's permission slip tonight")));
    }

    [Fact]
    public void TheWordCanBeAnywhereInTheTitleAndAnyCase()
    {
        Assert.Equal(TaskBand.Evening, TaskBands.For(Task("Put the BINS out")));
        Assert.Equal(TaskBand.Morning, TaskBands.For(Task("take vitamins")));
    }

    /// <summary>
    /// A word inside another word is not the word. "Morning" matching inside a title is fine;
    /// "bin" matching inside "binder" or "combine" is how a keyword rule quietly files half the
    /// list under Evening.
    /// </summary>
    [Fact]
    public void AWordInsideAnotherWordDoesNotCount()
    {
        Assert.Equal(TaskBand.DuringTheDay, TaskBands.For(Task("Combine the two drafts")));
        Assert.Equal(TaskBand.DuringTheDay, TaskBands.For(Task("Order a new binder")));
    }

    [Fact]
    public void TheHeadingsAreTheWordsInTheMockup()
    {
        Assert.Equal("Morning", TaskBands.Heading(TaskBand.Morning));
        Assert.Equal("During the day", TaskBands.Heading(TaskBand.DuringTheDay));
        Assert.Equal("Evening", TaskBands.Heading(TaskBand.Evening));
    }

    /// <summary>
    /// A task that was due earlier carries its reason on the line, because a list that silently
    /// re-presents yesterday's undone work looks like a list that invented it.
    /// </summary>
    [Fact]
    public void AnOverdueTaskSaysWhereItCameFrom()
    {
        Assert.Equal("moved from Saturday",
            TaskBands.Note(Task("Mow the lawn", Today.AddDays(-1)), Today));
    }

    [Fact]
    public void ATaskDueTodayOrLaterCarriesNoNote()
    {
        Assert.Equal(string.Empty, TaskBands.Note(Task("Call Mom", Today), Today));
        Assert.Equal(string.Empty, TaskBands.Note(Task("Call Mom", Today.AddDays(3)), Today));
        Assert.Equal(string.Empty, TaskBands.Note(Task("Call Mom"), Today));
    }

    /// <summary>
    /// Past a week the day name stops being useful — "moved from Tuesday" about something eleven
    /// days old reads as this Tuesday, which is worse than a date.
    /// </summary>
    [Fact]
    public void SomethingOlderThanAWeekIsDatedRatherThanNamed()
    {
        Assert.Equal("moved from Aug 30",
            TaskBands.Note(Task("Mow the lawn", new DateOnly(2026, 8, 30)), Today));
    }

    /// <summary>
    /// The kind marker — standing, smart, or a one-off — has no source yet: a Google Task is a
    /// title and a date. It is empty rather than guessed, so the page draws nothing rather than
    /// labelling everything "task", which would be a column of one repeated word.
    /// </summary>
    [Fact]
    public void TheKindIsEmptyUntilThereIsSomethingToReadItFrom()
    {
        Assert.Equal(string.Empty, TaskBands.Kind(Task("Call Mom")));
    }
}
