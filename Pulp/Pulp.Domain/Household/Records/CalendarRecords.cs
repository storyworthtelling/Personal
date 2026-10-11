namespace Pulp.Domain.Household.Records;

/// <summary>
/// The id of the household's Pulp calendar (D145): written once by the owner's install after it creates
/// the calendar, read by the other adult's install, which finds the calendar by this id and never makes one.
/// </summary>
public sealed record PulpCalendarEntry(string CalendarId);

internal sealed class PulpCalendarMap : RecordMap<PulpCalendarEntry>
{
    /// <summary>One calendar per household, so the record has one key.</summary>
    private const string OnlyId = "household";

    public override string Kind => HouseholdKinds.PulpCalendar;

    protected override string IdOf(PulpCalendarEntry value) => OnlyId;

    public override IReadOnlyDictionary<string, string> FieldsOf(PulpCalendarEntry value) =>
        new FieldWriter().Text("calendar", value.CalendarId).Fields;

    public override PulpCalendarEntry Read(RecordReader record) => new(record.Text("calendar"));
}
