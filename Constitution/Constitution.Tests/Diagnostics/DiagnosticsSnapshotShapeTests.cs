using System.Reflection;
using System.Text.RegularExpressions;
using Constitution.Domain.Diagnostics;

namespace Constitution.Tests.Diagnostics;

/// <summary>
/// The first line of defence for C-8.3.2, and a stronger one than a scan: the snapshot's type graph
/// has no member that <i>can</i> hold a reading, a name, a birthdate or a recipe. Walking the graph
/// from <see cref="DiagnosticsSnapshot"/> over every field, property and public constructor
/// parameter, the only leaves are integers (counts, capped), booleans, enums, and the four validated
/// identifiers — and no member is named for health or a person.
///
/// <para>A new field with free text on it fails here, before anything is rendered; widening the
/// allow-lists below is a decision about what the owner will be sharing, not a drift.</para>
/// </summary>
public class DiagnosticsSnapshotShapeTests
{
    /// <summary>Declaring type and member name of every string the graph may hold: the validated identifiers' single value each.</summary>
    private static readonly HashSet<string> AllowedStrings =
    [
        "DocumentFile.Value", "DocumentFile.<Value>k__BackingField",
        "ConnectionId.Value", "ConnectionId.<Value>k__BackingField",
        "AppVersion.Value", "AppVersion.<Value>k__BackingField",
        // Review S8, deliberately: a trail line is a fourth validated identifier. It can be made only through TrailLines.Filter,
        // which keeps a line whole or withholds it (TrailLinesTests plants weights, names, emails and long digit runs).
        "TrailLine.Value", "TrailLine.<Value>k__BackingField",
    ];

    /// <summary>The only integers: counts, each bounded by its constructor (see <c>ACountIsBounded</c>).</summary>
    private static readonly HashSet<string> AllowedCounts =
    [
        "WorkoutsPlanned", "WorkoutsDone", "RecipesInBox", "Count",
    ];

    /// <summary>A member with one of these words is a smell; the listed exceptions are counts only.</summary>
    private static readonly Regex Forbidden = new(
        @"(?i)reading|weight|calorie|kcal|name|birth|recipe|dish|ingredient|height|pulse|bpm|note|text|label|title|comment",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ForbiddenNameExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "RecipesInBox", "<RecipesInBox>k__BackingField", "recipesInBox",
    };

    private static readonly HashSet<Type> CollectionShapes =
    [
        typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>), typeof(IEnumerable<>), typeof(IList<>),
        typeof(List<>), typeof(System.Collections.ObjectModel.ReadOnlyCollection<>),
        typeof(System.Collections.Immutable.ImmutableArray<>),
    ];

    private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void TheWalkReachesTheWholeModel()
    {
        var visited = Walk(out _);

        string[] expected =
        [
            nameof(DiagnosticsSnapshot), nameof(DiagnosticsCounts), nameof(NoticeCount), nameof(ComponentOutcome),
            nameof(DocumentDelivery), nameof(DocumentFile), nameof(CredentialStatus), nameof(ConnectionId), nameof(AppVersion),
            nameof(TrailSection), nameof(TrailLine),
        ];
        foreach (var name in expected)
        {
            Assert.Contains(name, visited.Select(type => type.Name));
        }
    }

    [Fact]
    public void NoStringOutsideTheAllowList()
    {
        Walk(out var problems);

        Assert.True(problems.Count == 0, "The snapshot graph holds something that can carry free text or a value:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void NoMemberIsNamedForAReadingAPersonOrARecipe()
    {
        var offences = new List<string>();
        foreach (var type in Walk(out _))
        {
            foreach (var member in MembersOf(type))
            {
                if (Forbidden.IsMatch(member.Name) && !ForbiddenNameExceptions.Contains(member.Name))
                {
                    offences.Add($"{type.Name}.{member.Name}");
                }
            }
        }

        Assert.Empty(offences);
    }

    [Fact]
    public void TheRecipeCountIsTheOneExceptionAndIsAnInteger()
    {
        var member = typeof(DiagnosticsCounts).GetProperty(nameof(DiagnosticsCounts.RecipesInBox));

        Assert.NotNull(member);
        Assert.Equal(typeof(int), member!.PropertyType);
    }

    [Fact]
    public void EveryIntegerIsANamedCount()
    {
        var offences = new List<string>();
        foreach (var type in Walk(out _))
        {
            foreach (var member in MembersOf(type).Where(member => member.Type == typeof(int)))
            {
                var bare = member.Name.Trim('<', '>').Replace("k__BackingField", string.Empty).TrimEnd('>');
                var key = bare.Split('>')[0];
                if (!AllowedCounts.Contains(key) && !AllowedCounts.Contains(char.ToUpperInvariant(key[0]) + key[1..]))
                {
                    offences.Add($"{type.Name}.{member.Name}");
                }
            }
        }

        Assert.Empty(offences);
    }

    [Fact]
    public void NoPublicConstructorTakesAString()
    {
        var offences = new List<string>();
        foreach (var type in Walk(out _))
        {
            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var parameter in constructor.GetParameters().Where(parameter => parameter.ParameterType == typeof(string)))
                {
                    offences.Add($"{type.Name}({parameter.Name})");
                }
            }
        }

        Assert.Empty(offences);
    }

    [Fact]
    public void NoPublicSettersOrInitializersLetAValueBeChangedAfterValidation()
    {
        var offences = new List<string>();
        foreach (var type in Walk(out _))
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.SetMethod is { IsPublic: true })
                {
                    offences.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        Assert.Empty(offences);
    }

    [Fact]
    public void TheGuardCatchesAStringBeingAdded()
    {
        // Non-vacuous: the same rules, applied to a type that does carry free text, report it.
        var problems = new List<string>();
        Visit(typeof(LeakyForTesting), [], problems);

        Assert.Contains(problems, problem => problem.Contains("LeakyForTesting.Dish"));
        Assert.Contains(problems, problem => problem.Contains("LeakyForTesting.Grams"));
    }

    private sealed class LeakyForTesting
    {
        public string Dish { get; init; } = "";
        public double Grams { get; init; }
    }

    // ---- the walk -----------------------------------------------------------------------------

    // EqualityContract is the compiler's own record plumbing (a System.Type, never data); skipped by name.
    private sealed record Member(string Name, Type Type);

    private static IEnumerable<Member> MembersOf(Type type)
    {
        foreach (var field in type.GetFields(AllInstance))
        {
            yield return new Member(field.Name, field.FieldType);
        }
        foreach (var property in type.GetProperties(AllInstance).Where(property => property.Name != "EqualityContract"))
        {
            yield return new Member(property.Name, property.PropertyType);
        }
        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return new Member(parameter.Name ?? "?", parameter.ParameterType);
            }
        }
    }

    private static IReadOnlyList<Type> Walk(out List<string> problems)
    {
        problems = [];
        var visited = new List<Type>();
        Visit(typeof(DiagnosticsSnapshot), visited, problems);
        return visited;
    }

    private static void Visit(Type type, List<Type> visited, List<string> problems)
    {
        if (visited.Contains(type))
        {
            return;
        }
        visited.Add(type);

        foreach (var member in MembersOf(type))
        {
            Classify(type, member, visited, problems);
        }
    }

    private static void Classify(Type owner, Member member, List<Type> visited, List<string> problems)
    {
        var leaf = member.Type;
        if (leaf == typeof(string))
        {
            if (!AllowedStrings.Contains($"{owner.Name}.{member.Name}") && !IsParameterOfValidatedType(owner, member))
            {
                problems.Add($"{owner.Name}.{member.Name} is a string");
            }
            return;
        }

        if (leaf == typeof(int) || leaf == typeof(bool) || leaf.IsEnum)
        {
            return;
        }

        if (leaf.IsGenericType)
        {
            if (!CollectionShapes.Contains(leaf.GetGenericTypeDefinition()))
            {
                problems.Add($"{owner.Name}.{member.Name} is the unexpected generic {leaf.Name}");
                return;
            }
            foreach (var argument in leaf.GetGenericArguments())
            {
                Classify(owner, new Member(member.Name, argument), visited, problems);
            }
            return;
        }

        if (leaf.Assembly == typeof(DiagnosticsSnapshot).Assembly)
        {
            Visit(leaf, visited, problems);
            return;
        }

        problems.Add($"{owner.Name}.{member.Name} is an unexpected {leaf.Name}");
    }

    /// <summary>The private constructor of a validated identifier takes its one string; it is the only door, and it checks the shape.</summary>
    private static bool IsParameterOfValidatedType(Type owner, Member member) =>
        owner.Name is "DocumentFile" or "ConnectionId" or "AppVersion" or "TrailLine" && member.Name == "value"
        && owner.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0;
}
