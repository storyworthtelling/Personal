using System.Reflection;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;

namespace Pulp.Tests.Guard;

/// <summary>
/// The household file's record kinds, frozen once shipped (G1's style). A kind is half of every
/// record's key, written into a file both phones — and later the TV — read. Rename one and nothing
/// fails: the renamed reader simply finds no records of its kind, so a household's playlists, vetoes
/// or children's rules quietly vanish while the old records sit unread in the file forever.
///
/// <para>The expected values are typed out on purpose: the guard exists to notice the value moving,
/// so it carries its own record of what the value was.</para>
/// </summary>
public class HouseholdKindsTests
{
    private static readonly Dictionary<string, string> Frozen = new(StringComparer.Ordinal)
    {
        [nameof(HouseholdKinds.Watched)] = "watched",
        [nameof(HouseholdKinds.Playlist)] = "playlist",
        [nameof(HouseholdKinds.Veto)] = "veto",
        [nameof(HouseholdKinds.ChildStar)] = "child-star",
        [nameof(HouseholdKinds.ParentPick)] = "parent-pick",
        [nameof(HouseholdKinds.HomeOverride)] = "home-override",
        [nameof(HouseholdKinds.ChildRules)] = "child-rules",
        [nameof(HouseholdKinds.Viewer)] = "viewer",
        [nameof(HouseholdKinds.AttestedService)] = "attested-service",
        [nameof(HouseholdKinds.HouseholdService)] = "household-service",
    };

    private static IReadOnlyDictionary<string, string> Declared() =>
        typeof(HouseholdKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);

    [Fact]
    public void EveryKindHasItsFrozenValue()
    {
        var declared = Declared();
        Assert.NotEmpty(declared);

        foreach (var (name, value) in Frozen)
        {
            Assert.True(declared.TryGetValue(name, out var actual), $"HouseholdKinds.{name} is gone; its records would never be read again.");
            Assert.Equal(value, actual);
        }
    }

    /// <summary>A new kind is a decision, not a diff: it has to be added here, frozen, to ship.</summary>
    [Fact]
    public void NoKindShipsUnguarded()
    {
        var unguarded = Declared().Keys.Except(Frozen.Keys).ToList();
        Assert.True(unguarded.Count == 0, "Record kinds not frozen in this guard: " + string.Join(", ", unguarded));
    }

    [Fact]
    public void KindsAreDistinctAndKeyable()
    {
        var values = Declared().Values.ToList();
        Assert.Equal(Frozen.Count, values.Count);
        Assert.Equal(values.Count, values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var value in values)
        {
            // RecordKey lower-cases a kind; one written in another case would be two spellings of one key.
            Assert.Equal(value.ToLowerInvariant(), value);
            Assert.Equal(value, new RecordKey(value, "x").Kind);
        }
    }

    /// <summary>Every typed map writes under a frozen kind, and no two maps share one.</summary>
    [Fact]
    public void EveryMapWritesUnderAFrozenKind()
    {
        var maps = HouseholdMaps.Kinds;
        Assert.Equal(Frozen.Count, maps.Count);
        Assert.Equal(maps.Count, maps.Distinct(StringComparer.Ordinal).Count());
        Assert.All(maps, kind => Assert.Contains(kind, Frozen.Values));
    }
}
