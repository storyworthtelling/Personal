using Pulp.Domain.Core;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Household.Records;

/// <summary>
/// One adult following one team (D60: follows are per adult, D67: adding a team brings its league in).
/// <see cref="SportsFollow"/> itself is the Domain value; this map is its household record, keyed by
/// viewer and team so following twice is one record and unfollowing is its tombstone.
/// </summary>
internal sealed class SportsFollowMap : RecordMap<SportsFollow>
{
    public override string Kind => HouseholdKinds.SportsFollow;

    // A team id is compared without case everywhere, so the key is lower-cased: two spellings must not be two follows.
    protected override string IdOf(SportsFollow value) =>
        RecordIds.Join(value.Viewer.Value, value.Team.Value.ToLowerInvariant());

    public override IReadOnlyDictionary<string, string> FieldsOf(SportsFollow value) =>
        new FieldWriter().Text("viewer", value.Viewer.Value).Text("team", value.Team.Value).Fields;

    public override SportsFollow Read(RecordReader record) =>
        new(new ViewerId(record.Text("viewer")), new TeamId(record.Text("team")));
}
