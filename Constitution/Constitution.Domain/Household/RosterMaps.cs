using Constitution.Domain.Household.Records;

namespace Constitution.Domain.Household;

/// <summary>
/// The <c>person</c> record: id, name, role, pickup, optional Google address and optional birthdate. The
/// birthdate is written to the record's <c>birthdate</c> field and to no other; reading it back
/// reports a date it cannot parse without repeating it.
/// </summary>
internal sealed class PersonMap : RecordMap<RosterPerson>
{
    private static readonly FieldWords<MemberRole> Roles = new(new Dictionary<MemberRole, string>
    {
        [MemberRole.Adult] = "adult",
        [MemberRole.Child] = "child",
    });

    public override string Kind => ConstitutionKinds.Person;

    protected override string IdOf(RosterPerson value) => value.Id.Value;

    public override IReadOnlyDictionary<string, string> FieldsOf(RosterPerson value)
    {
        value.EnsureValid();
        return new FieldWriter()
            .Text("person", value.Id.Value)
            .Text("name", value.Name)
            .Word("role", Roles, value.Role)
            .Flag("pickup", value.OnPickup)
            .Text("google", value.GoogleAddress)
            .Date("birthdate", value.Birthdate?.Date)
            .Fields;
    }

    public override RosterPerson Read(RecordReader record)
    {
        try
        {
            return new RosterPerson(
                new PersonId(record.Text("person")),
                record.Text("name"),
                record.Word("role", Roles),
                record.OptionalDate("birthdate") is { } date ? new Birthdate(date) : null,
                record.OptionalFlag("pickup"),
                record.OptionalText("google"));
        }
        catch (ArgumentException exception)
        {
            // A person the rules refuse (a child on pickup, a bad address) is an unreadable record, not a crash.
            throw new HouseholdRecordException(record.Key, exception.Message);
        }
    }
}

/// <summary>The one map per record kind. Writers and readers both go through these, so a kind has one schema.</summary>
public static class RosterMaps
{
    public static RecordMap<RosterPerson> Person { get; } = new PersonMap();
}
