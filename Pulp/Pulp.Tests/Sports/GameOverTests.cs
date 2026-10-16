using System.Reflection;
using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>
/// D317 onward (see the Follow-up rulings): whether a game is over is a fact about the clock and nothing else.
/// It is the game's start plus a typical length for the league's sport. No score, winner, record or feed
/// status flag takes part (G15), and the table carries no member that could hold one.
/// </summary>
public class GameOverTests
{
    private static readonly LeagueId Nba = new("nba");
    private static readonly LeagueId Nfl = new("nfl");

    private static readonly string[] ResultWords =
    [
        "Score", "Result", "Winner", "Win", "Loser", "Loss", "Record", "Point", "Goal", "Run", "Outcome", "Margin",
        "Status", "Completed", "Final", "Finished", "Advanced", "Tied", "Streak",
    ];

    private static IEnumerable<string> WordsOf(string name) =>
        System.Text.RegularExpressions.Regex.Matches(name, "[A-Z]?[a-z]+|[A-Z]+(?![a-z])").Select(match => match.Value);

    [Fact]
    public void AGameIsOverOnceItsSportsTypicalLengthHasPassed()
    {
        var start = Now.AddHours(-3);

        Assert.True(GameOver.IsOver(Nba, start, Now));
        Assert.False(GameOver.IsOver(Nfl, start, Now));
    }

    [Fact]
    public void AGameIsNotOverBeforeItsLengthIsUpOrBeforeItStarts()
    {
        Assert.False(GameOver.IsOver(Nfl, Now.AddMinutes(-1), Now));
        Assert.False(GameOver.IsOver(Nfl, Now.AddHours(2), Now));
        Assert.True(GameOver.IsOver(Nfl, Now - GameOver.LengthOf(Nfl), Now));
    }

    [Fact]
    public void ALeagueTheTableDoesNotKnowTakesTheLongestAllowanceSoItIsNeverCalledOverEarly()
    {
        var longest = GameOver.Lengths.Values.Max();

        Assert.Equal(longest, GameOver.LengthOf(Coastal));
        Assert.True(GameOver.Lengths.Values.All(length => length > TimeSpan.Zero && length <= longest));
    }

    [Fact]
    public void TheTableIsKeyedWithoutCaseByLeague()
    {
        Assert.Equal(GameOver.LengthOf(new LeagueId("nba")), GameOver.LengthOf(new LeagueId("NBA")));
    }

    [Fact]
    public void NoMemberOfTheTimeRuleCouldHoldAResult()
    {
        var names = typeof(GameOver)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .Concat(typeof(GameOver).GetMethods(BindingFlags.Public | BindingFlags.Static).SelectMany(method => method.GetParameters()).Select(parameter => parameter.Name!))
            .ToList();

        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.DoesNotContain(WordsOf(name), word => ResultWords.Contains(word, StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ThePublicTimeRuleTakesOnlyALeagueAndTwoMoments()
    {
        foreach (var method in typeof(GameOver).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method => method.Name == nameof(GameOver.IsOver)))
        {
            Assert.All(method.GetParameters(), parameter => Assert.Contains(parameter.ParameterType, new[] { typeof(LeagueId), typeof(DateTimeOffset) }));
        }
    }

    [Fact]
    public void AGameLineAndARowHaveNoMemberNamedForAResult()
    {
        foreach (var type in new[] { typeof(GameLine), typeof(GameRow), typeof(Game) })
        {
            var names = type.GetProperties().Select(property => property.Name).ToList();
            Assert.NotEmpty(names);
            Assert.All(names, name => Assert.DoesNotContain(WordsOf(name), word => ResultWords.Contains(word, StringComparer.OrdinalIgnoreCase)));
        }
    }
}
