using System.Reflection;
using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealPlanning;

/// <summary>C-6.3.3: a favourites week by the same rules when the author is away, and nothing on the page says so.</summary>
public class MealFallbackTests
{
    private sealed class FakeAuthor(Func<MealWeekRequest, MealAuthorResult> answer) : IMealAuthor
    {
        public int Asked { get; private set; }

        public Task<MealAuthorResult> AuthorAsync(MealWeekRequest request, CancellationToken cancellation)
        {
            Asked++;
            return Task.FromResult(answer(request));
        }
    }

    private sealed class FakeLog : IMealLog
    {
        public List<(PlanOrigin Origin, AuthorUnavailable? Reason)> Entries { get; } = [];

        public void Record(PlanOrigin origin, AuthorUnavailable? reason) => Entries.Add((origin, reason));
    }

    [Fact]
    public async Task FavouritesWeekObeysTheRules()
    {
        var request = Request(childrenHome: AllEvenings());
        var log = new FakeLog();

        var outcome = await new MealWithFallback(new NoMealAuthor(), log).PlanAsync(request, CancellationToken.None);

        Assert.Equal(PlanOrigin.RulesOnly, outcome.Origin);
        Assert.Equal(AuthorUnavailable.NotConfigured, outcome.Reason);
        Assert.True(outcome.Week.IsPlanned);
        Assert.Empty(MealRules.Violations(request, outcome.Week));
    }

    [Fact]
    public async Task TheRulesWeekIsTheSameWeekThePlannerMakes()
    {
        var request = Request();

        var outcome = await new MealWithFallback(new NoMealAuthor(), new FakeLog()).PlanAsync(request, CancellationToken.None);

        Assert.Equal(MealWeekCodec.Write(MealPlanner.Plan(request)), MealWeekCodec.Write(outcome.Week));
    }

    [Fact]
    public async Task AGoodAuthoredWeekIsKept()
    {
        var request = Request();
        var good = MealPlanner.Plan(request with { Seed = 99 });
        var log = new FakeLog();

        var outcome = await new MealWithFallback(new FakeAuthor(_ => new MealAuthorResult.Authored(good)), log).PlanAsync(request, CancellationToken.None);

        Assert.Equal(PlanOrigin.Authored, outcome.Origin);
        Assert.Null(outcome.Reason);
        Assert.Same(good, outcome.Week);
        Assert.Equal([(PlanOrigin.Authored, (AuthorUnavailable?)null)], log.Entries);
    }

    [Fact]
    public async Task AnAuthoredWeekThatBreaksARuleIsReplaced()
    {
        var request = Request();
        var planned = MealPlanner.Plan(request);
        var bad = MealWeek.Planned(
            Monday,
            planned.Dinners.Select((dinner, index) => index == 1 ? DinnerChoice.Cooked(dinner.Day, planned.Dinners[0].Recipe!.Value, Sam, Edt(10, 27, 17), Edt(10, 27, 18), false, false, "Again.") : dinner));
        var log = new FakeLog();

        var outcome = await new MealWithFallback(new FakeAuthor(_ => new MealAuthorResult.Authored(bad)), log).PlanAsync(request, CancellationToken.None);

        Assert.Equal(PlanOrigin.RulesOnly, outcome.Origin);
        Assert.Equal(AuthorUnavailable.BrokeTheRules, outcome.Reason);
        Assert.NotSame(bad, outcome.Week);
        Assert.Empty(MealRules.Violations(request, outcome.Week));
        Assert.Equal([(PlanOrigin.RulesOnly, (AuthorUnavailable?)AuthorUnavailable.BrokeTheRules)], log.Entries);
    }

    [Fact]
    public async Task AnAuthoredWeekThatIsNotAWeekIsReplaced()
    {
        var request = Request();
        var empty = MealWeek.NotPlanned(Monday, MealStop.BoxTooSmall, "Nothing.");

        var outcome = await new MealWithFallback(new FakeAuthor(_ => new MealAuthorResult.Authored(empty)), new FakeLog()).PlanAsync(request, CancellationToken.None);

        Assert.Equal(AuthorUnavailable.BrokeTheRules, outcome.Reason);
        Assert.True(outcome.Week.IsPlanned);
    }

    [Theory]
    [InlineData(AuthorUnavailable.Offline)]
    [InlineData(AuthorUnavailable.TimedOut)]
    [InlineData(AuthorUnavailable.KeyRejected)]
    [InlineData(AuthorUnavailable.ReplyUnusable)]
    public async Task AnUnavailableAuthorsReasonIsKept(AuthorUnavailable reason)
    {
        var outcome = await new MealWithFallback(new FakeAuthor(_ => new MealAuthorResult.Unavailable(reason)), new FakeLog()).PlanAsync(Request(), CancellationToken.None);

        Assert.Equal(reason, outcome.Reason);
        Assert.Equal(PlanOrigin.RulesOnly, outcome.Origin);
    }

    [Fact]
    public async Task WhenNothingMayBePlannedTheAuthorIsNotAsked()
    {
        var request = Request(calendars: CalendarsWith(sam: CalendarRead.Unreadable("Not shared.")));
        var author = new FakeAuthor(_ => new MealAuthorResult.Authored(MealPlanner.Plan(Request())));
        var log = new FakeLog();

        var outcome = await new MealWithFallback(author, log).PlanAsync(request, CancellationToken.None);

        Assert.Equal(0, author.Asked);
        Assert.False(outcome.Week.IsPlanned);
        Assert.Equal(MealStop.CalendarUnreadable, outcome.Week.Stop);
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task AFailingAuthorIsNotSwallowed()
    {
        var author = new FakeAuthor(_ => throw new InvalidOperationException("a bug"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new MealWithFallback(author, new FakeLog()).PlanAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public void ThePageModelNeverSaysWhereTheWeekCameFrom()
    {
        var words = new[] { "Origin", "Author", "Rules", "Fallback", "Claude", "Model" };
        foreach (var type in new[] { typeof(MealWeek), typeof(DinnerChoice) })
        {
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(member => member.Name);
            Assert.DoesNotContain(members, name => words.Any(word => name.Contains(word, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task TheSerializedWeekIsIdenticalForAnyOrigin()
    {
        var request = Request();
        var planned = MealPlanner.Plan(request);

        var fromRules = await new MealWithFallback(new NoMealAuthor(), new FakeLog()).PlanAsync(request, CancellationToken.None);
        var fromAuthor = await new MealWithFallback(new FakeAuthor(_ => new MealAuthorResult.Authored(planned)), new FakeLog()).PlanAsync(request, CancellationToken.None);

        Assert.Equal(MealWeekCodec.Write(fromRules.Week), MealWeekCodec.Write(fromAuthor.Week));
    }
}
