using System.Reflection;
using System.Text.RegularExpressions;
using Pulp.Domain.Sports;

namespace Pulp.Tests.Sports;

/// <summary>
/// G10 — a held league's view model cannot carry a score. Not "the presenter leaves the score out":
/// the held view's type graph has no member that could hold one, so a careless presenter has
/// nowhere to put it. The same idea as Folio's nudge-never-write guard on <c>Mark</c>.
///
/// The walk fails on three things anywhere under <see cref="HeldLeague"/>: a member or type whose
/// name carries a result word (score, standing, winner, highlight, bracket …); a number of any kind
/// (a score is a number — a held league has dates and names, never a count); and an open door — an
/// interface, <c>object</c>, an unsealed class or a type from outside Pulp's Domain, any of which
/// could carry a score in through a subtype the walk cannot see.
/// </summary>
public class HeldLeagueCarriesNoScoreTests
{
    private static readonly HashSet<string> ResultWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Score", "Scores", "Scoreline", "Scored", "Result", "Results", "Standing", "Standings",
        "Table", "Winner", "Winners", "Win", "Wins", "Won", "Winning", "Loser", "Losers", "Loss",
        "Losses", "Lost", "Points", "Goals", "Runs", "Tally", "Rank", "Ranking", "Record", "Outcome",
        "Margin", "Bracket", "Seed", "Seeding", "Highlight", "Highlights", "Advanced", "Eliminated",
    };

    private static readonly HashSet<Type> SafeLeaves =
    [
        typeof(string), typeof(bool), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly),
    ];

    private static readonly HashSet<Type> Sequences =
    [
        typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>), typeof(IEnumerable<>),
    ];

    private static readonly Regex Words = new("[A-Z]?[a-z]+|[A-Z]+(?![a-z])|[0-9]+");

    [Fact]
    public void AHeldLeagueHasNoMemberThatCouldCarryAScore()
    {
        var walk = Walk(typeof(HeldLeague));

        // Not vacuous: the walk must have reached the games and the championship it is guarding.
        Assert.Contains(typeof(GameLine), walk.Visited);
        Assert.Contains(typeof(HeldChampionship), walk.Visited);
        Assert.Contains(typeof(TeamGames), walk.Visited);
        Assert.True(walk.MembersChecked >= 15, $"The walk checked only {walk.MembersChecked} members.");

        Assert.True(walk.Offences.Count == 0,
            "A held league's view model can carry a result:\n" + string.Join("\n", walk.Offences));
    }

    [Fact]
    public void TheWalkCatchesANamedScore()
    {
        Assert.NotEmpty(Walk(typeof(SampleWithNamedScore)).Offences);
    }

    [Fact]
    public void TheWalkCatchesANumberWhateverItIsCalled()
    {
        Assert.NotEmpty(Walk(typeof(SampleWithBareNumber)).Offences);
    }

    [Fact]
    public void TheWalkCatchesAnOpenDoor()
    {
        Assert.NotEmpty(Walk(typeof(SampleWithOpenDoor)).Offences);
    }

    [Fact]
    public void TheWalkCatchesAScoreInsideAList()
    {
        Assert.NotEmpty(Walk(typeof(SampleWithListedScore)).Offences);
    }

    [Fact]
    public void TheWalkDoesNotMistakeARecordingForARecord()
    {
        Assert.Empty(Walk(typeof(SampleWithRecording)).Offences);
    }

    private sealed record SampleWithNamedScore(string HomeScore);

    private sealed record SampleWithBareNumber(int Margin2, int Count);

    private sealed record SampleWithOpenDoor(object Anything);

    private sealed record SampleInner(string FinalResult);

    private sealed record SampleWithListedScore(IReadOnlyList<SampleInner> Rows);

    private sealed record SampleWithRecording(bool UnwatchedRecording);

    private sealed class WalkResult
    {
        public HashSet<Type> Visited { get; } = [];

        public List<string> Offences { get; } = [];

        public int MembersChecked { get; set; }
    }

    private static WalkResult Walk(Type root)
    {
        var result = new WalkResult();
        Visit(root, root.Name, result, root.Assembly);
        return result;
    }

    private static void Visit(Type type, string path, WalkResult result, Assembly home)
    {
        if (SafeLeaves.Contains(type))
        {
            return;
        }

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            Visit(underlying, path, result, home);
            return;
        }

        if (type.IsGenericType && Sequences.Contains(type.GetGenericTypeDefinition()))
        {
            Visit(type.GetGenericArguments()[0], path + "[]", result, home);
            return;
        }

        if (!result.Visited.Add(type))
        {
            return;
        }

        CheckName(type.Name, path + " (type)", result);

        if (type.IsPrimitive || type == typeof(decimal))
        {
            result.Offences.Add($"{path}: a number ({type.Name}); a held league carries no numbers");
            return;
        }

        if (type.Assembly != home && type.Assembly != typeof(HeldLeague).Assembly)
        {
            result.Offences.Add($"{path}: {type.FullName} comes from outside Pulp's Domain and cannot be walked");
            return;
        }

        if (type.IsEnum)
        {
            foreach (var name in Enum.GetNames(type))
            {
                CheckName(name, $"{path}.{name} (enum value)", result);
            }

            return;
        }

        if (type.IsInterface || type.IsAbstract || (type.IsClass && !type.IsSealed))
        {
            result.Offences.Add($"{path}: {type.Name} is open (interface, abstract or unsealed) and could carry anything");
            return;
        }

        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            result.MembersChecked++;
            CheckName(property.Name, $"{path}.{property.Name}", result);
            Visit(property.PropertyType, $"{path}.{property.Name}", result, home);
        }

        foreach (var field in type.GetFields(instance))
        {
            result.MembersChecked++;
            var name = field.Name.Replace("k__BackingField", string.Empty).Trim('<', '>');
            CheckName(name, $"{path}.{name} (field)", result);
            Visit(field.FieldType, $"{path}.{name}", result, home);
        }
    }

    private static void CheckName(string name, string path, WalkResult result)
    {
        var hit = Words.Matches(name).Select(match => match.Value).FirstOrDefault(ResultWords.Contains);
        if (hit is not null)
        {
            result.Offences.Add($"{path}: '{hit}' names a result");
        }
    }
}
