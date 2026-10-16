using System.Globalization;
using Pulp.Domain.Core;

namespace Pulp.Domain.Household.Records;

/// <summary>
/// A plan to go and see something (D68, D146): a cinema outing or a "plan to watch", on a day and
/// optionally a time. <see cref="Text"/> is what the calendar says; <see cref="Title"/> is set when the
/// plan is about a catalogue title and left null for free text. <see cref="Owner"/> is whoever made it and
/// <see cref="Who"/> everyone it is for — adults only reach the calendar (D244). Deleting it is its tombstone.
/// </summary>
public sealed record HouseholdPlan(
    string Id,
    ViewerId Owner,
    string Text,
    TitleId? Title,
    DateOnly Date,
    TimeOnly? Time,
    string? Place,
    IReadOnlyList<ViewerId> Who);

internal sealed class PlanMap : RecordMap<HouseholdPlan>
{
    private const string DayFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";

    public override string Kind => HouseholdKinds.Plan;

    protected override string IdOf(HouseholdPlan value) => RecordIds.Join(value.Id.ToLowerInvariant());

    public override IReadOnlyDictionary<string, string> FieldsOf(HouseholdPlan value) =>
        new FieldWriter()
            .Text("owner", value.Owner.Value)
            .Text("text", value.Text)
            .Text("title", value.Title?.Value)
            .Text("date", value.Date.ToString(DayFormat, CultureInfo.InvariantCulture))
            .Text("time", value.Time?.ToString(TimeFormat, CultureInfo.InvariantCulture))
            .Text("place", value.Place)
            .List("who", value.Who.Select(who => who.Value))
            .Fields;

    public override HouseholdPlan Read(RecordReader record)
    {
        var day = record.Text("date");
        if (!DateOnly.TryParseExact(day, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw record.Fail("date", $"is '{day}', not a day");
        }

        TimeOnly? time = null;
        if (record.OptionalText("time") is { } clock)
        {
            time = TimeOnly.TryParseExact(clock, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : throw record.Fail("time", $"is '{clock}', not a time of day");
        }

        return new HouseholdPlan(
            record.Key.Id,
            new ViewerId(record.Text("owner")),
            record.Text("text"),
            record.OptionalText("title") is { } title ? new TitleId(title) : null,
            date,
            time,
            record.OptionalText("place"),
            record.List("who").Select(who => new ViewerId(who)).ToList());
    }
}

/// <summary>
/// The Google address an adult signs in to the calendar with. It exists so the owner's install can share
/// the Pulp calendar with the other adult (D145, reader access); it lives only in the encrypted household
/// file. Its own record, keyed by viewer, rather than a field of the roster's, so editing it never touches a
/// roster record and an older build's roster reads are unaffected.
/// </summary>
public sealed record AdultAccountEntry(ViewerId Viewer, string Email);

internal sealed class AdultAccountMap : RecordMap<AdultAccountEntry>
{
    public override string Kind => HouseholdKinds.AdultAccount;

    protected override string IdOf(AdultAccountEntry value) => RecordIds.Join(value.Viewer.Value);

    public override IReadOnlyDictionary<string, string> FieldsOf(AdultAccountEntry value) =>
        new FieldWriter().Text("viewer", value.Viewer.Value).Text("email", value.Email).Fields;

    public override AdultAccountEntry Read(RecordReader record) =>
        new(new ViewerId(record.Text("viewer")), record.Text("email"));
}

/// <summary>What an address has to look like to be shared with: one local part, one @, a dotted domain, no spaces.</summary>
public static class GoogleAddress
{
    public const int Longest = 254;

    public const string Problem = "That doesn't look like an email address.";

    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Null when fine, else a plain sentence for the field.</summary>
    public static string? Check(string? text)
    {
        var address = Clean(text);
        if (address is null || address.Length > Longest || address.Any(char.IsWhiteSpace))
        {
            return Problem;
        }

        var at = address.IndexOf('@', StringComparison.Ordinal);
        if (at < 1 || at != address.LastIndexOf('@'))
        {
            return Problem;
        }

        var domain = address[(at + 1)..];
        var labels = domain.Split('.');
        return labels.Length >= 2 && labels.All(label => label.Length > 0) ? null : Problem;
    }
}
