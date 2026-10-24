using System.Reflection;
using Constitution.Domain.Household;

namespace Constitution.Tests.HouseholdFile;

/// <summary>
/// C-6.1.4 — a birthdate never reaches a list or Diagnostics. A scan of rendered text can only catch the
/// leak it was shown; this holds the shape instead, in the style of the diagnostics snapshot's shape
/// guard: apart from the two types that exist to carry one (the person as the owner edits it, and the
/// sealed value itself), no type in Constitution.Domain has a field, property or constructor
/// parameter that is a birthdate, or is named for one.
/// </summary>
public class BirthdateShapeTests
{
    private static readonly HashSet<string> MayCarryOne = [nameof(Birthdate), nameof(RosterPerson)];

    private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void OnlyThePersonAndTheValueCarryABirthdate()
    {
        var offenders = Offenders(typeof(RosterPerson).Assembly.GetTypes().Where(type => !MayCarryOne.Contains(type.Name)));

        Assert.True(offenders.Count == 0, "These Domain types can hold a birthdate; a list or a Diagnostics line could print it:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoDiagnosticsTypeCanHoldOne()
    {
        var types = typeof(RosterPerson).Assembly.GetTypes().Where(type => type.Namespace?.Contains("Diagnostics", StringComparison.Ordinal) == true).ToList();
        Assert.True(types.Count >= 5, "The guard found too few Diagnostics types to mean anything.");

        Assert.Empty(Offenders(types));
    }

    /// <summary>The shape guard sees types; this reads the files, so a Diagnostics page that reaches for the value by name is caught too.</summary>
    [Fact]
    public void NoDiagnosticsSourceNamesABirthdate()
    {
        var files = ConstitutionSource.Files(".cs")
            .Select(ConstitutionSource.Relative)
            .Where(path => !path.StartsWith("Constitution.Tests/", StringComparison.Ordinal))
            .Where(path => path.Contains("Diagnostics", StringComparison.Ordinal))
            .ToList();
        Assert.True(files.Count >= 5, "The guard found too few Diagnostics files to mean anything.");

        var root = ConstitutionSource.Root();
        var named = files
            .Where(path => File.ReadAllText(Path.Combine(root, path)).Contains("Birthdate", StringComparison.Ordinal))
            .ToList();

        Assert.True(named.Count == 0, "Diagnostics source names the birthdate type:\n" + string.Join("\n", named));
    }

    [Fact]
    public void TheRosterProjectionsCarryNone()
    {
        var views = new[] { typeof(RosterEntry), typeof(RosterView), typeof(RosterLabel), typeof(RosterEditResult), typeof(RosterRefusal), typeof(RosterEditor) };

        Assert.Empty(Offenders(views));
    }

    [Fact]
    public void TheScanFindsAPlantedOne()
    {
        Assert.NotEmpty(Offenders([typeof(PlantedByType)]));
        Assert.NotEmpty(Offenders([typeof(PlantedByName)]));
        Assert.NotEmpty(Offenders([typeof(PlantedInAList)]));
        Assert.NotEmpty(Offenders([typeof(PlantedInAConstructor)]));
        Assert.Empty(Offenders([typeof(PlantedInnocent)]));
    }

    [Fact]
    public void TheTypesThatMayCarryOneDo()
    {
        Assert.NotEmpty(Offenders([typeof(RosterPerson)]));
    }

    private static List<string> Offenders(IEnumerable<Type> types) =>
        types.SelectMany(type => Members(type).Select(member => type.FullName + "." + member)).ToList();

    private static IEnumerable<string> Members(Type type)
    {
        foreach (var member in type.GetMembers(AllInstance))
        {
            if (member.Name.Contains("birth", StringComparison.OrdinalIgnoreCase))
            {
                yield return member.Name;
            }
        }

        foreach (var field in type.GetFields(AllInstance).Where(field => Holds(field.FieldType)))
        {
            yield return field.Name;
        }

        foreach (var property in type.GetProperties(AllInstance).Where(property => Holds(property.PropertyType)))
        {
            yield return property.Name;
        }

        foreach (var parameter in type.GetConstructors(AllInstance).SelectMany(constructor => constructor.GetParameters()).Where(parameter => Holds(parameter.ParameterType)))
        {
            yield return "ctor(" + parameter.Name + ")";
        }
    }

    private static bool Holds(Type type) =>
        type == typeof(Birthdate)
        || (type.IsGenericType && type.GetGenericArguments().Any(Holds))
        || (type.IsArray && Holds(type.GetElementType()!));

    private sealed class PlantedByType { public Birthdate? Value { get; init; } }

    private sealed class PlantedByName { public string BirthDay { get; init; } = string.Empty; }

    private sealed class PlantedInAList { public IReadOnlyList<Birthdate> Values { get; init; } = []; }

    private sealed class PlantedInAConstructor { public PlantedInAConstructor(Birthdate value) { } }

    private sealed class PlantedInnocent { public string Name { get; init; } = string.Empty; }
}
