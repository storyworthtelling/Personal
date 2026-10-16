using System.Reflection;
using System.Text.RegularExpressions;
using Pulp.Domain.Sports;

namespace Pulp.Tests.Sports;

/// <summary>
/// G15 — no adapter surfaces a score into the Domain. Two halves, because a score can get in by either
/// door. <b>The contract:</b> everything <see cref="ISportsSource"/> can return is walked, and nothing in
/// that type graph has a member or type that names a result or is a number of any kind (a score is a
/// number; a schedule has dates and names). <b>The adapters:</b> their source is read, and no JSON
/// property they take off the wire is outside a written-down list of schedule, team, stage, channel and
/// rank fields, none is named like a result, and no identifier in them is — so a score cannot be read
/// "just to compare" and then dropped, because it is never read. The allowlist is the point: a new read
/// is a decision made here, in view, not a line added somewhere in an adapter.
/// </summary>
public class SportsSourcesCarryNoScoreTests
{
    private static readonly HashSet<string> ResultWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Score", "Scores", "Scoreline", "Scored", "Scoring", "Result", "Results", "Winner", "Winners", "Win", "Wins", "Won", "Winning",
        "Loser", "Losers", "Loss", "Losses", "Lost", "Point", "Points", "Goal", "Goals", "Run", "Runs", "Tally", "Record", "Records",
        "Outcome", "Margin", "Leader", "Leaders", "Streak", "Pct", "Differential", "Linescore", "Linescores", "Tie", "Tied",
        "Clinch", "Clinched", "Elimination", "Eliminated", "Status", "Completed", "Final", "Advanced",
    };

    private static readonly HashSet<Type> SafeLeaves = [typeof(string), typeof(DateTimeOffset), typeof(DateOnly)];

    private static readonly Regex Words = new("[A-Z]?[a-z]+|[A-Z]+(?![a-z])|[0-9]+");

    // The two standings containers are the wire's own names for "the list of teams"; they are read as containers
    // and only their team and rank are taken out of them.
    private static readonly HashSet<string> ContainerExceptions = new(StringComparer.Ordinal) { "records", "teamRecords" };

    private static readonly Dictionary<string, string[]> AllowedReads = new()
    {
        ["MlbStatsSource.cs"] =
        [
            "seasons", "springStartDate", "regularSeasonStartDate", "regularSeasonEndDate", "seasonEndDate",
            "teams", "id", "name", "dates", "games", "gamePk", "gameDate", "gameType", "seriesDescription",
            "home", "away", "team", "broadcasts", "type", "isNational", "records", "teamRecords", "sportRank",
        ],
        ["EspnSource.cs"] =
        [
            "sports", "leagues", "teams", "team", "id", "displayName", "logo", "logos", "href", "season", "startDate", "endDate",
            "type", "events", "date", "competitions", "competitors", "homeAway", "notes", "headline", "broadcasts", "market",
            "names", "children", "standings", "entries", "stats", "name", "value", "slug",
        ],
    };

    private static readonly Regex Reads = new(@"\.(?:Text|Number|Long|Flag|Child)\(""([^""]+)""\)|(?:TryGetProperty|GetProperty)\(""([^""]+)""", RegexOptions.Compiled);

    private static readonly Regex Literal = new(@"""(?:[^""\\]|\\.)*""", RegexOptions.Compiled);

    private static IEnumerable<string> Names(string text) => Words.Matches(text).Select(match => match.Value);

    // ---- the contract ----

    private static readonly MethodInfo[] SourceMethods = typeof(ISportsSource).GetMethods();

    [Fact]
    public void NothingASportsSourceCanReturnCouldCarryAScore()
    {
        Assert.NotEmpty(SourceMethods);
        var visited = new HashSet<Type>();
        var offences = new List<string>();
        var members = 0;
        foreach (var method in SourceMethods)
        {
            var returned = method.ReturnType;
            var carried = returned.IsGenericType && returned.GetGenericTypeDefinition() == typeof(Task<>) ? returned.GetGenericArguments()[0] : returned;
            Visit(carried, method.Name, visited, offences, ref members);
        }

        // Not vacuous: the walk reached the games, the teams, the championship and the league it is guarding.
        Assert.Contains(typeof(Game), visited);
        Assert.Contains(typeof(Team), visited);
        Assert.Contains(typeof(Championship), visited);
        Assert.Contains(typeof(League), visited);
        Assert.True(members >= 20, $"The walk checked only {members} members.");
        Assert.True(offences.Count == 0, "A sports source can return a result:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void NoMethodOrParameterOfASportsSourceNamesAResult()
    {
        foreach (var method in SourceMethods)
        {
            Assert.DoesNotContain(Names(method.Name.Replace("get_", string.Empty)), ResultWords.Contains);
            foreach (var parameter in method.GetParameters())
            {
                Assert.DoesNotContain(Names(parameter.Name!), ResultWords.Contains);
            }
        }
    }

    [Fact]
    public void TheWalkCatchesAScoreAndAnyNumberAndAnOpenDoor()
    {
        foreach (var sample in new[] { typeof(NamedScore), typeof(BareNumber), typeof(OpenDoor), typeof(ListedWinner) })
        {
            var offences = new List<string>();
            var members = 0;
            Visit(sample, sample.Name, [], offences, ref members);
            Assert.NotEmpty(offences);
        }
    }

    private sealed record NamedScore(string HomeScore);

    private sealed record BareNumber(int Count);

    private sealed record OpenDoor(object Anything);

    private sealed record Row(string Winner);

    private sealed record ListedWinner(IReadOnlyList<Row> Rows);

    private static void Visit(Type type, string path, HashSet<Type> visited, List<string> offences, ref int members)
    {
        if (SafeLeaves.Contains(type))
        {
            return;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            Visit(type.GetGenericArguments()[0], path + "[]", visited, offences, ref members);
            return;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            Visit(underlying, path, visited, offences, ref members);
            return;
        }

        if (!visited.Add(type))
        {
            return;
        }

        Check(type.Name, path + " (type)", offences);
        if (type.IsPrimitive || type == typeof(decimal))
        {
            offences.Add($"{path}: a number ({type.Name}); a schedule carries dates and names, never a count");
            return;
        }

        if (type.Assembly != typeof(ISportsSource).Assembly && type.Assembly != typeof(GameId).Assembly)
        {
            offences.Add($"{path}: {type.FullName} comes from outside Pulp's Domain and cannot be walked");
            return;
        }

        if (type.IsEnum)
        {
            foreach (var name in Enum.GetNames(type))
            {
                Check(name, $"{path}.{name} (enum value)", offences);
            }

            return;
        }

        if (type.IsInterface || type.IsAbstract || (type.IsClass && !type.IsSealed))
        {
            offences.Add($"{path}: {type.Name} is open and could carry anything");
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            members++;
            Check(property.Name, $"{path}.{property.Name}", offences);
            Visit(property.PropertyType, $"{path}.{property.Name}", visited, offences, ref members);
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            members++;
            var name = field.Name.Replace("k__BackingField", string.Empty).Trim('<', '>');
            Check(name, $"{path}.{name} (field)", offences);
            Visit(field.FieldType, $"{path}.{name}", visited, offences, ref members);
        }
    }

    private static void Check(string name, string path, List<string> offences)
    {
        var hit = Names(name).FirstOrDefault(ResultWords.Contains);
        if (hit is not null)
        {
            offences.Add($"{path}: '{hit}' names a result");
        }
    }

    // ---- the adapters ----

    private static IReadOnlyList<(string File, string Code, List<string> Literals)> Adapters()
    {
        var found = Directory.EnumerateFiles(Path.Combine(PulpSource.Root(), "Pulp.Infrastructure", "Sports"), "*.cs")
            .Where(file => AllowedReads.ContainsKey(Path.GetFileName(file)))
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(file =>
            {
                var literals = new List<string>();
                var code = Literal.Replace(File.ReadAllText(file), match =>
                {
                    literals.Add(match.Value.Trim('"'));
                    return "\"\"";
                });
                code = Regex.Replace(code, "//.*$", string.Empty, RegexOptions.Multiline);
                return (Path.GetFileName(file), code, literals);
            })
            .ToList();
        return found;
    }

    [Fact]
    public void BothAdaptersAreFoundAndRead()
    {
        var adapters = Adapters();

        Assert.Equal(AllowedReads.Keys.Order(), adapters.Select(adapter => adapter.File).Order());
        Assert.All(adapters, adapter => Assert.True(adapter.Literals.Count >= 20, $"{adapter.File}: only {adapter.Literals.Count} literals found."));
    }

    [Fact]
    public void EveryPropertyAnAdapterTakesOffTheWireIsOnTheAllowedList()
    {
        foreach (var (file, _, _) in Adapters())
        {
            var source = File.ReadAllText(Path.Combine(PulpSource.Root(), "Pulp.Infrastructure", "Sports", file));
            var reads = Reads.Matches(source).Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).ToList();

            Assert.True(reads.Count >= 20, $"{file}: only {reads.Count} wire reads found; the guard is not looking at the adapter.");
            var stray = reads.Distinct().Except(AllowedReads[file]).ToList();
            Assert.True(stray.Count == 0, $"{file} reads {string.Join(", ", stray)} off the wire, which the allowed list does not name.");
        }
    }

    [Fact]
    public void NoPropertyOrNameAnAdapterReadsOrHoldsIsNamedLikeAResult()
    {
        foreach (var (file, code, literals) in Adapters())
        {
            var read = literals.Where(text => Regex.IsMatch(text, "^[A-Za-z][A-Za-z0-9]*$") && !ContainerExceptions.Contains(text));
            var offences = read.Where(text => Names(text).Any(ResultWords.Contains)).ToList();
            Assert.True(offences.Count == 0, $"{file} holds the literal(s) {string.Join(", ", offences)}, named like a result.");

            var identifiers = Regex.Matches(code, "[A-Za-z_][A-Za-z0-9_]*").Select(match => match.Value).Distinct()
                .Where(name => name != "record" && Names(name).Any(ResultWords.Contains)).ToList();
            Assert.True(identifiers.Count == 0, $"{file} uses the identifier(s) {string.Join(", ", identifiers)}, named like a result.");
        }
    }

    [Fact]
    public void AnAdapterHasNoModelOfTheWireToParseAResultInto()
    {
        foreach (var (file, code, _) in Adapters())
        {
            Assert.DoesNotMatch(@"JsonSerializer|Deserialize|JsonPropertyName|JsonConverter", code);
            foreach (Match declared in Regex.Matches(code, @"\b(?:record|class|struct)\s+(\w+)"))
            {
                // The only types an adapter declares are its own, and none of them is a wire shape.
                Assert.Contains(declared.Groups[1].Value, new[] { file[..^3], "SeasonDates", "ScoreboardSeason", "EspnLeague", "EspnLeagues" });
            }
        }
    }

    [Fact]
    public void TheOneStatsArrayIsReadForARankByNameAndNowhereElse()
    {
        var espn = File.ReadAllText(Path.Combine(PulpSource.Root(), "Pulp.Infrastructure", "Sports", "EspnSource.cs"));
        var start = espn.IndexOf("private static double RankOf", StringComparison.Ordinal);
        Assert.True(start > 0, "EspnSource.RankOf is not where the guard expects it.");
        var end = espn.IndexOf("private async Task<IReadOnlyList<Team>> ReadTeamsAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);

        var outside = espn[..start] + espn[end..];
        Assert.DoesNotContain("\"value\"", outside);
        Assert.DoesNotContain("TryGetDouble", outside);
        Assert.Contains("\"rank\", \"playoffSeed\"", espn[start..end]);
    }

    // ---- the calendar adapter (D354) ----

    // A calendar file is not JSON, so the allowlist is of property names. A SUMMARY can hold a score and is read only to be
    // refused when it has one; DESCRIPTION, STATUS, COMMENT and the rest are never named in the adapter at all.
    private static readonly string[] CalendarFiles = ["PwhlCalendarSource.cs"];

    private static readonly HashSet<string> AllowedCalendarTokens = new(StringComparer.Ordinal)
    {
        "BEGIN", "END", "VCALENDAR", "VEVENT", "UID", "DTSTART", "SUMMARY",

        // Not a field: the league's own name, which the adapter prints on the league it returns.
        "PWHL",
    };

    [Fact]
    public void EveryAdapterInTheSportsFolderIsGuardedByOneOfTheTwoLists()
    {
        var unguarded = Directory.EnumerateFiles(Path.Combine(PulpSource.Root(), "Pulp.Infrastructure", "Sports"), "*.cs")
            .Select(Path.GetFileName)
            .Where(name => name!.EndsWith("Source.cs", StringComparison.Ordinal))
            .Except(["RoutedSportsSource.cs", "YouTubeHighlightsSource.cs"])
            .Where(name => !AllowedReads.ContainsKey(name!) && !CalendarFiles.Contains(name!))
            .ToList();

        Assert.True(unguarded.Count == 0, $"{string.Join(", ", unguarded)} reads a source the score guard does not look at.");
    }

    [Fact]
    public void ACalendarAdapterNamesOnlyTheThreeSchedulePropertiesAndNothingThatCouldHoldAResult()
    {
        foreach (var file in CalendarFiles)
        {
            var path = Path.Combine(PulpSource.Root(), "Pulp.Infrastructure", "Sports", file);
            Assert.True(File.Exists(path), $"{file} is not where the guard expects it.");
            var literals = new List<string>();
            var code = Literal.Replace(File.ReadAllText(path), match =>
            {
                literals.Add(match.Value.Trim('"'));
                return "\"\"";
            });
            code = Regex.Replace(code, "//.*$", string.Empty, RegexOptions.Multiline);

            var tokens = literals.Where(text => Regex.IsMatch(text, "^[A-Z][A-Z-]*$")).Distinct().ToList();
            Assert.Contains("SUMMARY", tokens);
            Assert.Contains("DTSTART", tokens);
            var stray = tokens.Except(AllowedCalendarTokens).ToList();
            Assert.True(stray.Count == 0, $"{file} names the calendar field(s) {string.Join(", ", stray)}, which the allowed list does not.");

            var offences = Regex.Matches(code, "[A-Za-z_][A-Za-z0-9_]*").Select(match => match.Value).Distinct()
                .Where(name => name != "record" && Names(name).Any(ResultWords.Contains)).ToList();
            Assert.True(offences.Count == 0, $"{file} uses the identifier(s) {string.Join(", ", offences)}, named like a result.");
            Assert.DoesNotMatch(@"JsonSerializer|Deserialize|JsonPropertyName|JsonConverter", code);
            foreach (Match declared in Regex.Matches(code, @"\b(?:record|class|struct)\s+(\w+)"))
            {
                // Its own class and the parsed calendar it holds between requests (Domain games and teams, no wire shape).
                Assert.Contains(declared.Groups[1].Value, new[] { file[..^3], "ParsedCalendar" });
            }
        }
    }
}
