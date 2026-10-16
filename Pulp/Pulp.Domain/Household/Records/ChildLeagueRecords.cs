using Pulp.Domain.Core;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Household.Records;

/// <summary>
/// A parent allowing one child one league (D59, D72, D317). Its own record, keyed by child and league,
/// rather than a field of the child's rules, so two parents ticking different leagues at once are two
/// edits that both survive the merge; un-allowing is its tombstone. <paramref name="Parent"/> is the
/// adult who ticked it, for the record only — it is not part of the key.
/// </summary>
public sealed record ChildLeagueAllowance(ViewerId Child, LeagueId League, ViewerId Parent, DateTimeOffset At);

internal sealed class ChildLeagueMap : RecordMap<ChildLeagueAllowance>
{
    public override string Kind => HouseholdKinds.ChildLeague;

    // A league id is compared without case everywhere, so the key is lower-cased: two spellings must not be two allowances.
    protected override string IdOf(ChildLeagueAllowance value) =>
        RecordIds.Join(value.Child.Value, value.League.Value.ToLowerInvariant());

    public override IReadOnlyDictionary<string, string> FieldsOf(ChildLeagueAllowance value) =>
        new FieldWriter()
            .Text("child", value.Child.Value)
            .Text("league", value.League.Value)
            .Text("parent", value.Parent.Value)
            .Instant("at", value.At)
            .Fields;

    public override ChildLeagueAllowance Read(RecordReader record) =>
        new(
            new ViewerId(record.Text("child")),
            new LeagueId(record.Text("league")),
            new ViewerId(record.Text("parent")),
            record.Instant("at"));
}
