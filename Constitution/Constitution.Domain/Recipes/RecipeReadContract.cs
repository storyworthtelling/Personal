using System.Text.Json;
using Codex.Domain.Policy;

namespace Constitution.Domain.Recipes;

/// <summary>What reading Plate's answer came to: the words, or the name of the first field that was refused (never its value).</summary>
public sealed record RecipeReadParseResult(RecipeReadPayload? Payload, string? RefusedField)
{
    public bool IsValid => Payload is not null;
}

/// <summary>
/// A recipe as Plate reads it off a card: a title, the ingredient lines and the step lines. <b>Words only.</b> There is no
/// member that could hold an image, a file or a URI, so nothing the owner photographed can ride in with it (PBI C-11.2.1,
/// C-6.5.3). The owner confirms these words in Constitution; only the confirmed words are ever kept.
/// </summary>
public sealed record RecipeReadPayload(string Title, IReadOnlyList<string> Ingredients, IReadOnlyList<string> Steps);

/// <summary>
/// The wire between Constitution and Plate for a recipe scan (PBI C-11.2.1, TF 1.8). Constitution starts Plate's recipe
/// activity with <see cref="Action"/> as an explicit component and reads the result's three string extras.
///
/// <para>Plate's package is read from <c>PolicyContext.PlatePackage</c>, the one place that frozen id may be spelled; everything
/// else is a literal of its own. Plate holds the same words in <c>Codex.Domain/Plate/ReadRequest.cs</c> and
/// <c>Contracts/RecipeReadContractTests</c> reads that file by content, so neither side can move without a test failing. The
/// permission is declared by Plate at signature level; Constitution only holds it.</para>
/// </summary>
public static class RecipeReadContract
{
    /// <summary>Plate's package, frozen so devices upgrade in place. The intent names it explicitly.</summary>
    public const string PlatePackage = PolicyContext.PlatePackage;

    /// <summary>The action Constitution starts the recipe read under.</summary>
    public const string Action = "com.storyworthtelling.plate.action.READ_RECIPE";

    /// <summary>Plate's recipe activity, named directly so nothing else can answer.</summary>
    public const string Activity = PlatePackage + ".RecipeReadActivity";

    /// <summary>The signature-level permission Plate declares and Constitution holds, so only a suite app can ask.</summary>
    public const string Permission = PlatePackage + ".READ_RECIPE";

    /// <summary>The recipe's name, a plain string. Left out when the card gave none.</summary>
    public const string TitleExtra = "title";

    /// <summary>The ingredient lines, a JSON array of strings in card order.</summary>
    public const string IngredientsExtra = "ingredients";

    /// <summary>The step lines, a JSON array of strings in card order, numbering already removed.</summary>
    public const string StepsExtra = "steps";

    /// <summary>The most lines a list may carry. A card with more is not a card; refusing is kinder than a runaway list.</summary>
    public const int MaxLines = 200;

    /// <summary>The longest single line, in characters.</summary>
    public const int MaxLineLength = 2000;

    /// <summary>The longest title, in characters.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>
    /// Reads Plate's three extras. A list that is not a JSON array of strings, a list or title past its bound, or an answer
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
            return Refused(IngredientsExtra);
        }

        if (!TryReadLines(stepsJson, out var steps))
        {
            return Refused(StepsExtra);
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
