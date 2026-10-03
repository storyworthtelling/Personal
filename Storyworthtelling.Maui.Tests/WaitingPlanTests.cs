using System.Net;
using System.Text.RegularExpressions;
using Storyworthtelling.Maui.State;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// What the Waiting page draws, decided with no view in it: the rows, their dates and role bars, and
/// the sentence a page with nothing waiting says. <c>WaitingView</c> only paints this.
///
/// <para>This is how "the page empties live to <i>Nothing waiting.</i>" is tested as behaviour rather
/// than as text in a file: a plan is built from the state, the state's last notice is withdrawn, and
/// the plan built again is the empty one.</para>
/// </summary>
public class WaitingPlanTests
{
    // Past the week boundary of the suite's relative-time rule (§14), where "Friday" gives way to "Sep 25".
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Noon(int month, int day) => new(2026, month, day, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheLastNoticeResolvingLeavesThePageSayingNothingIsWaiting()
    {
        var state = new DogEarState();
        state.SetNotice("lost", new DogEarNotice("Meridian Card needs signing in", "d", DogEarHue.Problem, At: Noon(9, 22)));

        var before = WaitingPlan.From(state.Notices, Now);
        Assert.Single(before.Rows);
        Assert.Null(before.EmptySentence);

        state.SetNotice("lost", null);

        var after = WaitingPlan.From(state.Notices, Now);
        Assert.Empty(after.Rows);
        Assert.Equal("Nothing waiting.", after.EmptySentence);
    }

    [Fact]
    public void RowsFollowTheStatesOrder_NewestFirst()
    {
        var state = new DogEarState();
        state.SetNotice("lost", new DogEarNotice("Lost", "d", DogEarHue.Problem, At: Noon(9, 22)));
        state.SetNotice("stale", new DogEarNotice("Stale", "d", DogEarHue.Caution, At: Noon(9, 25)));
        state.SetNotice("trial", new DogEarNotice("Trial", "d", DogEarHue.Reference, At: Noon(9, 12)));

        Assert.Equal(["Stale", "Lost", "Trial"], WaitingPlan.From(state.Notices, Now).Rows.Select(r => r.Headline));
    }

    [Fact]
    public void EachRowsBarWearsTheTokenOfItsRole()
    {
        var notices = new[]
        {
            new DogEarNotice("p", "d", DogEarHue.Problem),
            new DogEarNotice("c", "d", DogEarHue.Caution),
            new DogEarNotice("r", "d", DogEarHue.Reference),
            new DogEarNotice("g", "d", DogEarHue.Progress),
        };

        Assert.Equal(
            ["ProblemMark", "CautionMark", "ReferenceMark", "ProgressMark"],
            WaitingPlan.From(notices, Now).Rows.Select(r => r.BarToken));
    }

    [Fact]
    public void ANoticeWithoutADatePrintsNoDate_AndOneWithoutAnActionNoButton()
    {
        var row = WaitingPlan.From([new DogEarNotice("h", "d", DogEarHue.Progress)], Now).Rows.Single();

        Assert.Null(row.Date);
        Assert.Null(row.ActionLabel);
    }

    [Fact]
    public void ANoticesActionTravelsWithItsRow()
    {
        var invoked = false;
        var notice = new DogEarNotice("h", "d", DogEarHue.Problem, "Reconnect Meridian Card", () => invoked = true);

        var row = WaitingPlan.From([notice], Now).Rows.Single();
        row.OnAction!();

        Assert.Equal("Reconnect Meridian Card", row.ActionLabel);
        Assert.True(invoked);
    }

    // ---- dates: the suite's relative-time rule, as the mockup draws it ----

    [Fact]
    public void RecentDatesAreRelativeAndOlderOnesAreAbsolute()
    {
        Assert.Equal("2h ago", NoticeDates.Label(Now.AddHours(-2), Now));
        Assert.Equal("yesterday", NoticeDates.Label(Now.AddDays(-1), Now));
        Assert.Equal("Sep 25", NoticeDates.Label(Noon(9, 25), Now));
        Assert.Null(NoticeDates.Label(null, Now));
    }

    /// <summary>
    /// The three dates frame 1 of mockup 17 draws beside its notices, read from the drawing: a notice
    /// dated that day, seen when the mockup is drawn, prints exactly what the mockup prints.
    /// </summary>
    [Fact]
    public void TheDatesPrintTheWayMockup17DrawsThem()
    {
        var html = File.ReadAllText(RepoFile("Ledger/design/mockup-17.html"));
        var drawn = Regex.Matches(html, @"<span class=""wp-w"">(?<w>.*?)</span>")
            .Select(m => WebUtility.HtmlDecode(m.Groups["w"].Value))
            .ToList();
        Assert.Equal(["Sep 25", "Sep 22", "Sep 12"], drawn);

        foreach (var text in drawn)
        {
            var at = DateTimeOffset.Parse($"{text} 2026 12:00 +00:00", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(text, NoticeDates.Label(at, Now));
        }
    }

    private static string RepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
