using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Household.Records;

/// <summary>
/// The <c>rating</c> record: person, card, day, verdict and an optional written reason. <b>Every field name and word
/// here is frozen</b> (<c>TasteMapTests</c> holds each as a literal). The id is person, card and day, so rating the
/// same dish the same day twice is one record edited, never two.
/// </summary>
internal sealed class RatingMap : RecordMap<TasteRating>
{
    public override string Kind => ConstitutionKinds.Rating;

    protected override string IdOf(TasteRating value) =>
        RecordIds.Join(value.Person.Value, value.Recipe.Value, RecordDates.Write(value.Day));

    public override IReadOnlyDictionary<string, string> FieldsOf(TasteRating value)
    {
        value.EnsureValid();
        return new FieldWriter()
            .Text("person", value.Person.Value)
            .Text("recipe", value.Recipe.Value)
            .Date("day", value.Day)
            .Word("verdict", TasteWords.Verdicts, value.Verdict)
            .Text("reason", value.Reason)
            .Fields;
    }

    public override TasteRating Read(RecordReader record)
    {
        try
        {
            return new TasteRating(
                new PersonId(record.Text("person")),
                new RecipeId(record.Text("recipe")),
                record.OptionalDate("day") ?? throw record.Fail("day", "is missing"),
                record.Word("verdict", TasteWords.Verdicts),
                record.OptionalText("reason"));
        }
        catch (ArgumentException exception)
        {
            // A rating the rules refuse (a bad id, a reason too long) is an unreadable record, not a crash.
            throw new HouseholdRecordException(record.Key, exception.Message);
        }
    }
}

/// <summary>
/// The <c>taste-note</c> record: person, subject kind and subject, stance, where it came from and the day it was
/// learned. Frozen words and field names, as for <see cref="RatingMap"/>. One record per person, kind and subject, so
/// changing your mind edits the one record.
/// </summary>
internal sealed class TasteNoteMap : RecordMap<TasteNote>
{
    public override string Kind => ConstitutionKinds.TasteNote;

    protected override string IdOf(TasteNote value) =>
        RecordIds.Join(value.Person.Value, TasteNoteWords.Kinds.Word(value.Kind), value.Subject);

    public override IReadOnlyDictionary<string, string> FieldsOf(TasteNote value)
    {
        value.EnsureValid();
        return new FieldWriter()
            .Text("person", value.Person.Value)
            .Word("kind", TasteNoteWords.Kinds, value.Kind)
            .Text("subject", value.Subject)
            .Word("stance", TasteNoteWords.Stances, value.Stance)
            .Word("origin", TasteNoteWords.Origins, value.Origin)
            .Date("learned", value.Learned)
            .Fields;
    }

    public override TasteNote Read(RecordReader record)
    {
        try
        {
            return new TasteNote(
                new PersonId(record.Text("person")),
                record.Word("kind", TasteNoteWords.Kinds),
                record.Text("subject"),
                record.Word("stance", TasteNoteWords.Stances),
                record.Word("origin", TasteNoteWords.Origins),
                record.OptionalDate("learned") ?? throw record.Fail("learned", "is missing"));
        }
        catch (ArgumentException exception)
        {
            // A note the rules refuse (an unknown cuisine, a blank subject) is an unreadable record, not a crash.
            throw new HouseholdRecordException(record.Key, exception.Message);
        }
    }
}

/// <summary>The one map per record kind for ratings and taste notes, beside <see cref="RosterMaps"/> and <see cref="RecipeMaps"/>.</summary>
public static class TasteMaps
{
    public static RecordMap<TasteRating> Rating { get; } = new RatingMap();

    public static RecordMap<TasteNote> Note { get; } = new TasteNoteMap();
}
