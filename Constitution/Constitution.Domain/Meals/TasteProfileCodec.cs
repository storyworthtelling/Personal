using System.Text;
using System.Text.Json;
using Constitution.Domain.Household;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals;

/// <summary>What reading a stored profile gave: the profile, or a plain reason it cannot be used. The reason quotes nothing from the document.</summary>
public abstract record TasteProfileRead
{
    public sealed record Read(TasteProfile Profile) : TasteProfileRead;

    public sealed record Unreadable(string Reason) : TasteProfileRead;
}

/// <summary>
/// The profile as JSON: <c>{"version":1,"kidFriendly":[id…],"people":[{"person","child","loved":[id…],"fine":[id…],
/// "neverAgain":[id…],"cuisines":{word:int},"ingredients":{key:number},"dishes":{id:int}}]}</c>. Names are frozen
/// literals; keys are written in ordinal order so the same profile is the same text; unknown keys are ignored. It holds
/// ids, keys and numbers only: no written reason, no person's name.
/// </summary>
public static class TasteProfileCodec
{
    private const int Version = 1;

    public const string NotJson = "The saved taste profile is not valid JSON.";
    public const string WrongShape = "The saved taste profile is not in a shape this version reads.";

    public static string Write(TasteProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            Ids(writer, "kidFriendly", profile.KidFriendly);
            writer.WriteStartArray("people");
            foreach (var taste in profile.People)
            {
                writer.WriteStartObject();
                writer.WriteString("person", taste.Person.Value);
                writer.WriteBoolean("child", taste.IsChild);
                Ids(writer, "loved", taste.Loved);
                Ids(writer, "fine", taste.Fine);
                Ids(writer, "neverAgain", taste.NeverAgain);
                writer.WriteStartObject("cuisines");
                foreach (var pair in taste.Cuisines.OrderBy(pair => Cuisines.Words.Word(pair.Key), StringComparer.Ordinal))
                {
                    writer.WriteNumber(Cuisines.Words.Word(pair.Key), pair.Value);
                }

                writer.WriteEndObject();
                writer.WriteStartObject("ingredients");
                foreach (var pair in taste.Ingredients.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteNumber(pair.Key, pair.Value);
                }

                writer.WriteEndObject();
                writer.WriteStartObject("dishes");
                foreach (var pair in taste.Dishes.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
                {
                    writer.WriteNumber(pair.Key.Value, pair.Value);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static TasteProfileRead Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new TasteProfileRead.Unreadable(NotJson);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return new TasteProfileRead.Read(ReadProfile(document.RootElement));
        }
        catch (JsonException)
        {
            return new TasteProfileRead.Unreadable(NotJson);
        }
        catch (TasteShapeException)
        {
            return new TasteProfileRead.Unreadable(WrongShape);
        }
    }

    private static TasteProfile ReadProfile(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var number)
            || number != Version)
        {
            throw new TasteShapeException();
        }

        var people = new List<PersonTaste>();
        foreach (var item in Items(root, "people"))
        {
            people.Add(ReadPerson(item));
        }

        return new TasteProfile(people, ReadIds(root, "kidFriendly"));
    }

    private static PersonTaste ReadPerson(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("person", out var person) || person.ValueKind != JsonValueKind.String
            || !item.TryGetProperty("child", out var child) || child.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new TasteShapeException();
        }

        var cuisines = new Dictionary<Cuisine, int>();
        foreach (var pair in Entries(item, "cuisines"))
        {
            cuisines[Cuisines.Words.TryValue(pair.Name, out var cuisine) ? cuisine : throw new TasteShapeException()] = WholeNumber(pair.Value);
        }

        var ingredients = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var pair in Entries(item, "ingredients"))
        {
            ingredients[pair.Name] = pair.Value.ValueKind == JsonValueKind.Number && pair.Value.TryGetDecimal(out var value) ? value : throw new TasteShapeException();
        }

        var dishes = new Dictionary<RecipeId, int>();
        foreach (var pair in Entries(item, "dishes"))
        {
            dishes[Recipe(pair.Name)] = WholeNumber(pair.Value);
        }

        try
        {
            return new PersonTaste(
                new PersonId(person.GetString()!),
                child.GetBoolean(),
                ReadIds(item, "loved"),
                ReadIds(item, "fine"),
                ReadIds(item, "neverAgain"),
                cuisines,
                ingredients,
                dishes);
        }
        catch (ArgumentException)
        {
            // A person id this build refuses is a shape it does not read, not a crash.
            throw new TasteShapeException();
        }
    }

    private static List<RecipeId> ReadIds(JsonElement owner, string name)
    {
        var ids = new List<RecipeId>();
        foreach (var element in Items(owner, name))
        {
            ids.Add(element.ValueKind == JsonValueKind.String ? Recipe(element.GetString()!) : throw new TasteShapeException());
        }

        return ids;
    }

    private static RecipeId Recipe(string text)
    {
        try
        {
            return new RecipeId(text);
        }
        catch (ArgumentException)
        {
            // A card id this build refuses is a shape it does not read, not a crash.
            throw new TasteShapeException();
        }
    }

    private static IEnumerable<JsonElement> Items(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : throw new TasteShapeException();
    }

    private static IEnumerable<JsonProperty> Entries(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        return value.ValueKind == JsonValueKind.Object ? value.EnumerateObject().ToList() : throw new TasteShapeException();
    }

    private static int WholeNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : throw new TasteShapeException();

    private static void Ids(Utf8JsonWriter writer, string name, IReadOnlyList<RecipeId> ids)
    {
        writer.WriteStartArray(name);
        foreach (var id in ids.OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            writer.WriteStringValue(id.Value);
        }

        writer.WriteEndArray();
    }

    /// <summary>Carries "the shape is wrong" from the readers to <see cref="Read"/>, which turns it into a result that quotes nothing.</summary>
    private sealed class TasteShapeException : Exception
    {
    }
}
