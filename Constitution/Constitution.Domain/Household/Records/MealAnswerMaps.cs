using System.Globalization;
using Constitution.Domain.Meals;
using Storyworthtelling.Household;

namespace Constitution.Domain.Household.Records;

/// <summary>
/// The record keys of the meal answers, under the frozen <c>week-plan</c> kind. An answer's id is its person, its week and its
/// line, behind the word <c>answer</c> so that the plan itself (a later record of the same kind) can never share a key with
/// one. Only the install that reads a person's tablet writes that person's answers, so two installs never write one key.
/// </summary>
public static class MealAnswerKeys
{
    internal const string Part = "answer";

    public static RecordKey KeyOf(MealAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return new RecordKey(
            ConstitutionKinds.WeekPlan,
            RecordIds.Join(Part, answer.Person.Value, RecordDates.Write(answer.Week), answer.ItemId));
    }

    /// <summary>Whether the key is a meal answer's. A record of the same kind that is not one is left alone, neither read nor reported.</summary>
    public static bool IsAnswer(RecordKey key) =>
        key.Kind == ConstitutionKinds.WeekPlan && key.Id.StartsWith(Part + RecordIds.Separator, StringComparison.Ordinal);
}

/// <summary>
/// The <c>week-plan</c> answer record: person, week, item, verdict, reason and the moment it was read. <b>Every field name and
/// verdict word is frozen</b> (<c>MealAnswerMapTests</c> holds each as a literal): a renamed one is a reader that finds nothing
/// while the answers sit in Drive.
/// </summary>
internal sealed class MealAnswerMap : RecordMap<MealAnswer>
{
    private static readonly FieldWords<MealVerdict> Verdicts = new(new Dictionary<MealVerdict, string>
    {
        [MealVerdict.Approved] = "approved",
        [MealVerdict.Denied] = "denied",
        [MealVerdict.Unchanged] = "unchanged",
    });

    public override string Kind => ConstitutionKinds.WeekPlan;

    protected override string IdOf(MealAnswer value) => MealAnswerKeys.KeyOf(value).Id;

    public override IReadOnlyDictionary<string, string> FieldsOf(MealAnswer value) =>
        new FieldWriter()
            .Text("person", value.Person.Value)
            .Date("week", value.Week)
            .Text("item", value.ItemId)
            .Word("verdict", Verdicts, value.Verdict)
            .Text("reason", value.Reason)
            .Text("read", value.ReadAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture))
            .Fields;

    public override MealAnswer Read(RecordReader record)
    {
        MealAnswer value;
        try
        {
            value = new MealAnswer(
                new PersonId(record.Text("person")),
                record.OptionalDate("week") ?? throw record.Fail("week", "is missing"),
                record.Text("item"),
                record.Word("verdict", Verdicts),
                record.OptionalText("reason"),
                ReadAt(record));
        }
        catch (ArgumentException exception)
        {
            // An answer the rules refuse (a reason on a tick, a week that is not a Sunday) is an unreadable record, not a crash.
            throw new HouseholdRecordException(record.Key, exception.Message);
        }

        return KeyOf(value) == record.Key ? value : throw record.Fail("item", "does not belong to this record's key");
    }

    private static DateTimeOffset ReadAt(RecordReader record)
    {
        if (!long.TryParse(record.Text("read"), NumberStyles.None, CultureInfo.InvariantCulture, out var millis))
        {
            throw record.Fail("read", "is not a whole number");
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(millis);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw record.Fail("read", "is not a time the calendar holds");
        }
    }
}

/// <summary>The one map for meal answers, beside <see cref="RecipeMaps"/>.</summary>
public static class MealAnswerMaps
{
    public static RecordMap<MealAnswer> Answer { get; } = new MealAnswerMap();
}
