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

    private static ForewordTask Task(string title, TaskKind kind = TaskKind.Once) =>
        new() { Id = title, Title = title, Kind = kind };

    [Fact]
    public void MostThingsHappenDuringTheDay()
    {
        Assert.Equal(TaskBand.DuringTheDay, TaskBands.For(Task("Call Mom")));
        Assert.Equal(TaskBand.DuringTheDay, TaskBands.For(Task("Order Gwen's cleats")));
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
        Assert.Equal(TaskBand.Evening, TaskBands.For(Task("Sign Gwen's permission slip tonight")));
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
    /// The marker says how the task behaves rather than where it came from. Origin is not the
    /// interesting thing about a task on a page — what it will do next morning is.
    /// </summary>
    [Fact]
    public void TheKindIsTheBehaviour()
    {
        Assert.Equal("rule", TaskBands.Kind(Task("Vitamins", TaskKind.Rule)));
        Assert.Equal("smart", TaskBands.Kind(Task("Mow the lawn", TaskKind.Smart)));
        Assert.Equal("once", TaskBands.Kind(Task("Call Mom")));
    }
}
