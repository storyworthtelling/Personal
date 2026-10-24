using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Household.Records;

/// <summary>
/// The <c>recipe</c> record: id, title, cuisine, the ingredient and step lists, servings, source label and the
/// kids flag. <b>Every field name and word here is frozen</b> (the file outlives the build; <c>RecipeMapTests</c>
/// holds each as a literal): a renamed field is a reader that finds nothing while the cards sit in Drive.
///
/// <para>The two lists are each one field of JSON, so an edit that shortens a list replaces it whole — separate
/// indexed fields would leave the removed lines behind, since an edit only overwrites the fields it names. The
/// names inside the JSON are frozen the same way. Reading refuses anything it cannot make a valid card of, and
/// says which field, never what it held.</para>
/// </summary>
internal sealed class RecipeMap : RecordMap<Recipe>
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public override string Kind => ConstitutionKinds.Recipe;

    protected override string IdOf(Recipe value) => value.Id.Value;

    public override IReadOnlyDictionary<string, string> FieldsOf(Recipe value)
    {
        value.EnsureValid();
        return new FieldWriter()
            .Text("recipe", value.Id.Value)
            .Text("title", value.Title)
            .Word("cuisine", Cuisines.Words, value.Cuisine)
            .Text("ingredients", WriteIngredients(value.Ingredients))
            .Text("steps", WriteSteps(value.Steps))
            .Text("servings", value.Servings?.ToString(CultureInfo.InvariantCulture))
            .Text("source", value.SourceLabel)
            .Flag("kids", value.ForKids)
            .Fields;
    }

    public override Recipe Read(RecordReader record)
    {
        try
        {
            return new Recipe(
                new RecipeId(record.Text("recipe")),
                record.Text("title"),
                record.Word("cuisine", Cuisines.Words),
                ReadIngredients(record),
                ReadSteps(record),
                ReadServings(record),
                record.OptionalText("source"),
                record.OptionalFlag("kids"));
        }
        catch (ArgumentException exception)
        {
            // A card the rules refuse (no title, an address for a source) is an unreadable record, not a crash.
            throw new HouseholdRecordException(record.Key, exception.Message);
        }
    }

    private static int? ReadServings(RecordReader record)
    {
        var text = record.OptionalText("servings");
        if (text is null)
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var servings)
            ? servings
            : throw record.Fail("servings", "is not a whole number");
    }

    private static string WriteIngredients(IReadOnlyList<Ingredient> ingredients)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartArray();
            foreach (var ingredient in ingredients)
            {
                writer.WriteStartObject();
                writer.WriteString("quantity", ingredient.Quantity ?? string.Empty);
                writer.WriteString("unit", ingredient.Unit ?? string.Empty);
                writer.WriteString("name", ingredient.Name);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string WriteSteps(IReadOnlyList<RecipeStep> steps)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartArray();
            foreach (var step in steps)
            {
                writer.WriteStartObject();
                writer.WriteString("text", step.Text);
                if (step.Minutes is { } minutes)
                {
                    writer.WriteNumber("minutes", minutes);
                }
                else
                {
                    writer.WriteNull("minutes");
                }

                writer.WriteBoolean("active", step.Active);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static List<Ingredient> ReadIngredients(RecordReader record)
    {
        var ingredients = new List<Ingredient>();
        foreach (var item in Items(record, "ingredients", "a list of ingredients"))
        {
            ingredients.Add(new Ingredient(
                OptionalString(record, item, "ingredients", "quantity"),
                OptionalString(record, item, "ingredients", "unit"),
                OptionalString(record, item, "ingredients", "name") ?? throw record.Fail("ingredients", "has a line without a name")));
        }

        return ingredients;
    }

    private static List<RecipeStep> ReadSteps(RecordReader record)
    {
        var steps = new List<RecipeStep>();
        foreach (var item in Items(record, "steps", "a list of steps"))
        {
            steps.Add(new RecipeStep(
                OptionalString(record, item, "steps", "text") ?? throw record.Fail("steps", "has a step without words"),
                OptionalMinutes(record, item),
                OptionalActive(record, item)));
        }

        return steps;
    }

    private static List<JsonElement> Items(RecordReader record, string field, string shape)
    {
        var text = record.OptionalText(field);
        if (text is null)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw record.Fail(field, "is not " + shape);
            }

            var items = new List<JsonElement>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                items.Add(element.ValueKind == JsonValueKind.Object ? element.Clone() : throw record.Fail(field, "is not " + shape));
            }

            return items;
        }
        catch (JsonException)
        {
            throw record.Fail(field, "is not " + shape);
        }
    }

    private static string? OptionalString(RecordReader record, JsonElement item, string field, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw record.Fail(field, "has a '" + name + "' that is not words");
    }

    private static int? OptionalMinutes(RecordReader record, JsonElement item)
    {
        if (!item.TryGetProperty("minutes", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var minutes)
            ? minutes
            : throw record.Fail("steps", "has minutes that are not a whole number");
    }

    private static bool OptionalActive(RecordReader record, JsonElement item)
    {
        if (!item.TryGetProperty("active", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw record.Fail("steps", "has an active mark that is not true or false");
    }
}

/// <summary>The one map per record kind for recipes, beside <see cref="RosterMaps"/>.</summary>
public static class RecipeMaps
{
    public static RecordMap<Recipe> Recipe { get; } = new RecipeMap();
}
