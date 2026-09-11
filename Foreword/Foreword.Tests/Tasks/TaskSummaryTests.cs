using Foreword.Domain.Tasks;
using Xunit;

namespace Foreword.Tests.Tasks;

/// <summary>
/// A task's rule written out as fields (T4, and the rows T7/T8 render).
///
/// <para>It is the same function on both screens by design. T4 shows a freshly parsed rule back
/// before anything is saved, and T7 is the form for editing it later — if those two disagreed about
/// what a rule says, the owner would accept one thing and find another.</para>
///
/// <para>The form <b>renders only the rows the kind actually uses</b>. That is the answer to the
/// objection against forms: a form that grows to the rule is fine, a form showing every rule's
/// controls to every task is not. "Vitamins, every day" has no window to place itself in, so it has
/// no window row.</para>
/// </summary>
public class TaskSummaryTests
{
    private static readonly ForewordTask Lawn = new()
    {
        Id = "lawn",
        Title = "Mow the lawn",
        Kind = TaskKind.Smart,
        Window = TaskWindow.Weekend,
        Prefers = DayOfWeek.Saturday,
        DryHoursNeeded = 3,
        Season = new SeasonRange(4, 10),
        IfNoDay = NoDayFallback.PrintAnyway,
    };

    private static readonly ForewordTask Vitamins = new()
    {
        Id = "vitamins",
        Title = "Vitamins",
        Kind = TaskKind.Rule,
        Repeat = Repeat.Daily,
    };

    private static string Value(ForewordTask task, string label) =>
        TaskSummary.Rows(task).Single(row => row.Label == label).Value;

    [Fact]
    public void ASmartRuleShowsEveryPartOfItself()
    {
        var labels = TaskSummary.Rows(Lawn).Select(row => row.Label).ToList();

        Assert.Equal(["Window", "Prefers", "Needs", "Season", "Decided", "If no dry day"], labels);
    }

    [Fact]
    public void TheValuesAreTheWordsTheMockupUses()
    {
        Assert.Equal("The weekend", Value(Lawn, "Window"));
        Assert.Equal("Saturday", Value(Lawn, "Prefers"));
        Assert.Equal("3 dry hours", Value(Lawn, "Needs"));
        Assert.Equal("April – October", Value(Lawn, "Season"));
    }

    /// <summary>
    /// The two consequences the owner would not have said out loud: when the decision gets made, and
    /// what happens when no day qualifies. They are on T4 precisely because they were never typed —
    /// a rule whose fallback the owner has not seen is one that will surprise them in the rain.
    /// </summary>
    [Fact]
    public void TheTwoUnsaidConsequencesAreShown()
    {
        Assert.Equal("When the window opens", Value(Lawn, "Decided"));
        Assert.Equal("Prints Saturday anyway", Value(Lawn, "If no dry day"));
    }

    [Fact]
    public void TheOtherFallbackReadsAsSkippingTheWeek()
    {
        Assert.Equal("Skips the week", Value(Lawn with { IfNoDay = NoDayFallback.SkipTheWeek }, "If no dry day"));
    }

    /// <summary>
    /// Three rows for "vitamins every day", because that is all it has to say. No window, no
    /// preferred day, no dry-hours stepper, no fallback — a daily task has no window to place itself
    /// in, so those controls would sit blank and mean nothing.
    /// </summary>
    [Fact]
    public void ADailyRuleShowsOnlyWhatItUses()
    {
        var labels = TaskSummary.Rows(Vitamins).Select(row => row.Label).ToList();

        Assert.Equal(["How often", "Season"], labels);
        Assert.Equal("Every day", Value(Vitamins, "How often"));
        Assert.Equal("All year", Value(Vitamins, "Season"));
    }

    [Fact]
    public void AWeeklyRuleNamesItsDay()
    {
        var bins = Vitamins with { Title = "Bins out", Repeat = Repeat.Weekly, On = DayOfWeek.Sunday };

        Assert.Equal("Every Sunday", Value(bins, "How often"));
    }

    /// <summary>
    /// A once task from Google opens shorter still, and what it shows is what Google holds: a due
    /// date. Its title and date are written back rather than kept here, so the two cannot disagree.
    /// </summary>
    [Fact]
    public void AOnceTaskShowsItsDueDateAndNothingElse()
    {
        var once = new ForewordTask
        {
            Id = "g-1", Title = "Call the dentist", Kind = TaskKind.Once,
            GoogleTaskId = "g-1", Due = new DateOnly(2026, 9, 18),
        };

        Assert.Equal(["Due"], TaskSummary.Rows(once).Select(row => row.Label));
        Assert.Equal("Friday 18 September", Value(once, "Due"));
    }

    [Fact]
    public void AOnceTaskWithNoDueDateSaysSo()
    {
        var once = new ForewordTask { Id = "x", Title = "Call Mom", Kind = TaskKind.Once };

        Assert.Equal("No date", Value(once, "Due"));
    }

    /// <summary>A season that wraps the new year still reads as a season rather than as nonsense.</summary>
    [Fact]
    public void AWrappedSeasonReadsForwards()
    {
        Assert.Equal("November – February",
            Value(Lawn with { Season = new SeasonRange(11, 2) }, "Season"));
    }

    [Fact]
    public void OneDryHourIsSingular()
    {
        Assert.Equal("1 dry hour", Value(Lawn with { DryHoursNeeded = 1 }, "Needs"));
    }

    /// <summary>
    /// A smart task that does not care about the weather has no Needs row. The row exists to carry a
    /// condition; with no condition it would say "0 dry hours", which reads as a requirement of none
    /// rather than as no requirement.
    /// </summary>
    [Fact]
    public void ASmartTaskWithNoWeatherConditionHasNoNeedsRow()
    {
        var rows = TaskSummary.Rows(Lawn with { DryHoursNeeded = null });

        Assert.DoesNotContain(rows, row => row.Label == "Needs");
    }
}
