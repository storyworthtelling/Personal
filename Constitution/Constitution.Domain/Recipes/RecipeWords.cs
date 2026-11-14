using System.Text.Json;

namespace Constitution.Domain.Recipes;

/// <summary>What reading a card's words came to: the words, or the name of the first field that was refused (never its value).</summary>
public sealed record RecipeReadParseResult(RecipeReadPayload? Payload, string? RefusedField)
{
    public bool IsValid => Payload is not null;
}

/// <summary>
/// A recipe's words: a title, the ingredient lines and the step lines. <b>Words only.</b> There is no member that could hold an
/// image, a file or a URI, so nothing photographed is ever kept with the card (C-6.5.3, D176). The owner checks and edits these
/// words in the three boxes; only what they keep is written.
/// </summary>
public sealed record RecipeReadPayload(string Title, IReadOnlyList<string> Ingredients, IReadOnlyList<string> Steps);

/// <summary>
/// The bounds a card's words are held to, wherever they came from: read off photographs of the card (D176) or typed into the three
/// boxes. One parse, so a card from either way in is refused for the same reasons, naming the field and never its value.
/// </summary>
public static class RecipeWords
{
    /// <summary>The most lines a list may carry. A card with more is not a card; refusing is kinder than a runaway list.</summary>
    public const int MaxLines = 200;

    /// <summary>The longest single line, in characters.</summary>
    public const int MaxLineLength = 2000;

    /// <summary>The longest title, in characters.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>
    /// Reads a card's three parts, the lists as JSON arrays of strings. A list that is not a JSON array of strings, a list or title past its bound, or an answer
    /// with neither an ingredient nor a step is refused whole, naming the field (<c>title</c>, <c>ingredients</c>,
    /// <c>steps</c>, or <c>recipe</c> for an answer with nothing in it) and never the value. Blank lines are dropped. A missing
    /// extra reads as empty.
    /// </summary>
    public static RecipeReadParseResult Parse(string? title, string? ingredientsJson, string? stepsJson)
    {
        var cleanTitle = (title ?? string.Empty).Trim();
        if (cleanTitle.Length > MaxTitleLength)
        {
            return Refused("title");
        }

        if (!TryReadLines(ingredientsJson, out var ingredients))
        {
            return Refused("ingredients");
        }

        if (!TryReadLines(stepsJson, out var steps))
        {
            return Refused("steps");
        }

        // A title alone is not a recipe, and keeping one would only give the owner something to throw away.
        return ingredients.Count == 0 && steps.Count == 0
            ? Refused("recipe")
            : new RecipeReadParseResult(new RecipeReadPayload(cleanTitle, ingredients, steps), null);
    }

    private static RecipeReadParseResult Refused(string field) => new(null, field);

    private static bool TryReadLines(string? json, out List<string> lines)
    {
        lines = [];
        if (json is null)
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > MaxLines)
            {
                return false;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var line = element.GetString()!.Trim();
                if (line.Length > MaxLineLength)
                {
                    return false;
                }

                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }

            return true;
        }
        catch (JsonException)
        {
            // Not JSON at all: refused by field name, as any other malformed list is. The text itself is never echoed.
            return false;
        }
    }
}
