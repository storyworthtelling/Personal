using Foreword.Domain.Tasks;
using Foreword.Domain.Weather;
using Xunit;

namespace Foreword.Tests.Tasks;

/// <summary>
/// Which tasks print this morning, and what the line says about why.
///
/// <para>This is the whole of "smart": the owner writes a rule once and never places it again. A
/// smart task chooses its own day inside a window, from the season, the forecast and its preference
/// — and the page has to be able to say <i>why</i> it chose today, because a list that schedules
/// itself and will not explain itself is one nobody trusts twice.</para>
/// </summary>
public class TaskPlacementTests
{
    // A Saturday.
    private static readonly DateOnly Saturday = new(2026, 9, 12);
    private static readonly DateOnly Sunday = new(2026, 9, 13);
    private static readonly DateOnly Wednesday = new(2026, 9, 16);

    private static ForewordTask Rule(string title, Repeat repeat = Repeat.Daily, DayOfWeek? on = null) =>
        new() { Id = title, Title = title, Kind = TaskKind.Rule, Repeat = repeat, On = on };

    private static ForewordTask Once(string title, DateOnly? due = null, bool done = false) =>
        new() { Id = title, Title = title, Kind = TaskKind.Once, Due = due, CompletedInGoogle = done };

    private static ForewordTask Lawn(
        TaskWindow window = TaskWindow.Weekend,
        DayOfWeek? prefers = DayOfWeek.Saturday,
        int? dryHours = 3,
        NoDayFallback ifNoDay = NoDayFallback.PrintAnyway,
        SeasonRange? season = null) => new()
        {
            Id = "lawn",
            Title = "Mow the lawn",
            Kind = TaskKind.Smart,
            Window = window,
            Prefers = prefers,
            DryHoursNeeded = dryHours,
            IfNoDay = ifNoDay,
            Season = season ?? new SeasonRange(4, 10),
        };

    // A week's forecast: how many consecutive dry hours each day offers.
    private static Func<DateOnly, int> DryHours(params (DateOnly Day, int Hours)[] days) =>
        date => days.FirstOrDefault(d => d.Day == date).Hours;

    private static Func<DateOnly, int> Dry(int hours) => _ => hours;

    // ---- rules ----

    [Fact]
    public void ADailyRulePrintsEveryDay()
    {
        var printed = TaskPlacement.ForToday([Rule("Vitamins")], Wednesday, Dry(8), AwayPause.None);

        Assert.Equal("Vitamins", printed.Single().Task.Title);
    }

    [Fact]
    public void AWeeklyRulePrintsOnlyOnItsDay()
    {
        var bins = Rule("Bins out", Repeat.Weekly, DayOfWeek.Sunday);

        Assert.Single(TaskPlacement.ForToday([bins], Sunday, Dry(8), AwayPause.None));
        Assert.Empty(TaskPlacement.ForToday([bins], Wednesday, Dry(8), AwayPause.None));
    }

    /// <summary>
    /// A recurring task does not fire while the owner is away. It would print on a page nobody is
    /// reading and come back as a backlog they did not fail to do.
    /// </summary>
    [Fact]
    public void ARecurringTaskIsPausedWhileAway()
    {
        var away = new AwayPause { From = Saturday, Until = Sunday };

        Assert.Empty(TaskPlacement.ForToday([Rule("Vitamins")], Saturday, Dry(8), away));
    }

    /// <summary>
    /// And it is not late afterwards. The mornings simply pass and the next one is ordinary — no
    /// catch-up, no pile, no "you missed eight of these".
    /// </summary>
    [Fact]
    public void APausedTaskIsNotLateAfterwards()
    {
        var away = new AwayPause { From = Saturday, Until = Sunday };

        var printed = TaskPlacement.ForToday([Rule("Vitamins")], Sunday.AddDays(1), Dry(8), away);

        Assert.Single(printed);
        Assert.DoesNotContain("late", printed[0].Note, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("missed", printed[0].Note, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The owner can leave the recurring tasks running while away; the toggle is theirs.</summary>
    [Fact]
    public void RecurringTasksKeepPrintingWhenThePauseIsOff()
    {
        var away = new AwayPause { From = Saturday, Until = Sunday, PauseRecurring = false };

        Assert.Single(TaskPlacement.ForToday([Rule("Vitamins")], Saturday, Dry(8), away));
    }

    /// <summary>
    /// A once task has a date of its own, so it keeps printing while away — it did not come from a
    /// rule, and a flight or a ranger talk is exactly the sort of thing a trip is made of.
    /// </summary>
    [Fact]
    public void SomethingWithADateOfItsOwnStillPrintsWhileAway()
    {
        var away = new AwayPause { From = Saturday, Until = Sunday };

        Assert.Single(TaskPlacement.ForToday([Once("Check in for the flight", Saturday)], Saturday, Dry(8), away));
    }

    // ---- once tasks ----

    [Fact]
    public void AOnceTaskPrintsOnAndAfterItsDueDate()
    {
        var task = Once("Call the dentist", Saturday);

        Assert.Empty(TaskPlacement.ForToday([task], Saturday.AddDays(-1), Dry(8), AwayPause.None));
        Assert.Single(TaskPlacement.ForToday([task], Saturday, Dry(8), AwayPause.None));
        Assert.Single(TaskPlacement.ForToday([task], Saturday.AddDays(3), Dry(8), AwayPause.None));
    }

    [Fact]
    public void AOnceTaskWithNoDueDatePrintsUntilItIsDone()
    {
        Assert.Single(TaskPlacement.ForToday([Once("Call Mom")], Wednesday, Dry(8), AwayPause.None));
    }

    /// <summary>
    /// Closed in Google's own app: it does not print. The list screen still shows it struck through
    /// so the owner can see why — but the sheet is the day's work, and this is not work any more.
    /// </summary>
    [Fact]
    public void ATaskClosedInGoogleDoesNotPrint()
    {
        Assert.Empty(TaskPlacement.ForToday(
            [Once("Pick up the dry cleaning", Saturday, done: true)], Saturday, Dry(8), AwayPause.None));
    }

    // ---- smart tasks ----

    /// <summary>
    /// The lawn on a dry Saturday: its window, its preferred day, and enough dry hours. All three
    /// agree, so it prints, and the line says which.
    /// </summary>
    [Fact]
    public void TheLawnPrintsOnADrySaturday()
    {
        var printed = TaskPlacement.ForToday([Lawn()], Saturday, Dry(6), AwayPause.None);

        Assert.Equal("Mow the lawn", printed.Single().Task.Title);
    }

    /// <summary>
    /// A wet Saturday moves it to Sunday rather than printing it into the rain. This is the whole
    /// point of a smart task: it places itself, and the owner never touches it.
    /// </summary>
    [Fact]
    public void AWetSaturdayMovesItToSunday()
    {
        var forecast = DryHours((Saturday, 1), (Sunday, 5));

        Assert.Empty(TaskPlacement.ForToday([Lawn()], Saturday, forecast, AwayPause.None));
        Assert.Single(TaskPlacement.ForToday([Lawn()], Sunday, forecast, AwayPause.None));
    }

    /// <summary>
    /// A whole wet weekend, with the fallback set to print anyway: Saturday gets it. The rule was a
    /// preference, not a condition, and the owner would rather be asked on the day they said.
    /// </summary>
    [Fact]
    public void AWetWeekendStillPrintsOnThePreferredDayWhenThatIsTheFallback()
    {
        var forecast = Dry(0);

        var printed = TaskPlacement.ForToday([Lawn()], Saturday, forecast, AwayPause.None);

        Assert.Single(printed);
        Assert.Contains("no dry", printed[0].Note, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And with the other fallback, the week passes. Some things genuinely cannot be done in the
    /// rain, and printing them anyway is how a list starts being ignored.
    /// </summary>
    [Fact]
    public void AWetWeekendCanSkipTheWeekInstead()
    {
        var lawn = Lawn(ifNoDay: NoDayFallback.SkipTheWeek);

        Assert.Empty(TaskPlacement.ForToday([lawn], Saturday, Dry(0), AwayPause.None));
        Assert.Empty(TaskPlacement.ForToday([lawn], Sunday, Dry(0), AwayPause.None));
    }

    /// <summary>Out of season it does not print at all, whatever the weather does.</summary>
    [Fact]
    public void OutOfSeasonItDoesNotPrint()
    {
        var january = new DateOnly(2027, 1, 9);   // a Saturday

        Assert.Empty(TaskPlacement.ForToday([Lawn()], january, Dry(8), AwayPause.None));
    }

    /// <summary>A weekend task does not place itself on a Wednesday, however dry.</summary>
    [Fact]
    public void AWeekendTaskStaysInItsWindow()
    {
        Assert.Empty(TaskPlacement.ForToday([Lawn()], Wednesday, Dry(10), AwayPause.None));
    }

    /// <summary>
    /// It prints once, not every day of its window. A task that appeared on both Saturday and Sunday
    /// would be a task the owner does twice or ignores once.
    /// </summary>
    [Fact]
    public void ItPrintsOnOneDayOfTheWindowOnly()
    {
        var forecast = Dry(6);

        Assert.Single(TaskPlacement.ForToday([Lawn()], Saturday, forecast, AwayPause.None));
        Assert.Empty(TaskPlacement.ForToday([Lawn()], Sunday, forecast, AwayPause.None));
    }

    /// <summary>
    /// With no preference, the first day of the window that qualifies takes it. Waiting for a better
    /// one is how a task spends the whole week being deferred to tomorrow.
    /// </summary>
    [Fact]
    public void WithNoPreferenceTheFirstQualifyingDayTakesIt()
    {
        var anyDay = Lawn(window: TaskWindow.Weekend, prefers: null);
        var forecast = Dry(6);

        Assert.Single(TaskPlacement.ForToday([anyDay], Saturday, forecast, AwayPause.None));
        Assert.Empty(TaskPlacement.ForToday([anyDay], Sunday, forecast, AwayPause.None));
    }

    /// <summary>A smart task is a recurring one, so it pauses while away like any other.</summary>
    [Fact]
    public void ASmartTaskIsPausedWhileAway()
    {
        var away = new AwayPause { From = Saturday, Until = Sunday };

        Assert.Empty(TaskPlacement.ForToday([Lawn()], Saturday, Dry(6), away));
    }

    /// <summary>
    /// The line says why it is here today, because a list that schedules itself and will not explain
    /// itself is one nobody trusts twice.
    /// </summary>
    [Fact]
    public void TheLineSaysWhyItIsHere()
    {
        var moved = TaskPlacement.ForToday([Lawn()], Sunday, DryHours((Saturday, 1), (Sunday, 5)), AwayPause.None);

        Assert.Contains("Saturday", moved.Single().Note, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An unparsed task — typed on a dead network — prints as an ordinary once task rather than
    /// waiting to be understood. The sentence is the owner's; holding it back until a model has read
    /// it would lose the morning it was written for.
    /// </summary>
    [Fact]
    public void AnUnparsedTaskStillPrints()
    {
        var unparsed = new ForewordTask
        {
            Id = "u", Title = "mow the lawn this weekend if dry",
            Kind = TaskKind.Once, Unparsed = true,
            Written = "mow the lawn this weekend if dry",
        };

        Assert.Single(TaskPlacement.ForToday([unparsed], Wednesday, Dry(8), AwayPause.None));
    }
}
