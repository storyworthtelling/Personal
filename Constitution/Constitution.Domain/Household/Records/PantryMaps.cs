using Constitution.Domain.Groceries;

namespace Constitution.Domain.Household.Records;

/// <summary>
/// The <c>staple</c> record: the item's name, its aisle word, whether it is out (<c>need</c>) and whether it is a one-off item in stock
/// rather than a standing staple (<c>stock</c>; absent reads as a standing staple). <b>Every field name and word is frozen</b>
/// (<c>PantryMapTests</c> holds each as a literal). The id is the ingredient's comparison key, so "Olive oil" and "olive oils" are one
/// record and a change of spelling does not make a second.
/// </summary>
internal sealed class StapleMap : RecordMap<Staple>
{
    public override string Kind => ConstitutionKinds.Staple;

    protected override string IdOf(Staple value) => RecordIds.Join(IngredientNames.Key(value.Name));

    public override IReadOnlyDictionary<string, string> FieldsOf(Staple value)
    {
        value.EnsureValid();
        return new FieldWriter()
            .Text("staple", value.Name)
            .Word("aisle", Aisles.Words, value.Aisle)
            .Flag("need", value.NeedMore)
            .Flag("stock", !value.Standing)
            .Fields;
    }

    public override Staple Read(RecordReader record)
    {
        try
        {
            return new Staple(
                record.Text("staple"),
                record.Word("aisle", Aisles.Words),
                record.OptionalFlag("need"),
                standing: !record.OptionalFlag("stock"));
        }
        catch (ArgumentException exception)
        {
            // A name the rules refuse is an unreadable record, not a crash.
            throw new HouseholdRecordException(record.Key, exception.Message);
        }
    }
}

/// <summary>The one map for the <c>staple</c> record kind, beside <see cref="RecipeMaps"/>.</summary>
public static class PantryMaps
{
    public static RecordMap<Staple> Staple { get; } = new StapleMap();
}
