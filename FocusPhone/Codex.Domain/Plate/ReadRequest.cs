using System.Text.Encodings.Web;
using System.Text.Json;
using Codex.Domain.Policy;
using Codex.Domain.Post;

namespace Codex.Domain.Plate;

/// <summary>
/// The inbound half of the handoff: another app asks, Plate looks, Plate returns.
///
/// <para>The design walkthrough moved <b>reading</b> from the outbound side to this one.
/// A statement read starts in Index; a card read starts in Post. Plate never works out what
/// a piece of paper is for — it is asked, it reads, it returns — which is the simpler
/// contract, because the app that wants the data already knows what it wants.</para>
///
/// <para>Two actions rather than one with a "kind" extra: a caller that sends the wrong
/// kind gets an activity-not-found it can see, instead of a result it cannot use.</para>
///
/// <para><b>Index does not exist yet</b> — it is P11 — so Plate's end of the statement read
/// ships with no live counterpart, and the on-device test plan has to say so rather than
/// imply a passing check.</para>
/// </summary>
public static class ReadRequest
{
    public const string ReadCard = "com.storyworthtelling.plate.action.READ_CARD";
    public const string ReadStatement = "com.storyworthtelling.plate.action.READ_STATEMENT";

    // The recipe read (PBI C-11.2.1). Constitution holds these same words as literals of its own
    // (Recipes/RecipeReadContract) and a guard there reads this file by content, so neither side can
    // move without a test failing. The package comes from PolicyContext.PlatePackage, the one place the
    // frozen id is allowed to be spelled (FrozenInvariantsTests); the guards resolve the same expression.

    /// <summary>Read a recipe card over 1-N pages and return its words. Answered only by <see cref="RecipeActivity"/>.</summary>
    public const string ReadRecipe = "com.storyworthtelling.plate.action.READ_RECIPE";

    /// <summary>
    /// The component the caller names. It is its own activity, behind <see cref="RecipePermission"/>, because the
    /// camera filters on the errand activity must stay open to every app and a permission cannot be set per filter.
    /// </summary>
    public const string RecipeActivity = PolicyContext.PlatePackage + ".RecipeReadActivity";

    /// <summary>The signature-level permission the caller must hold. Free: every head already shares one keystore.</summary>
    public const string RecipePermission = PolicyContext.PlatePackage + ".READ_RECIPE";

    public const string TitleExtra = "title";

    /// <summary>The ingredient lines as a JSON array of strings, in card order.</summary>
    public const string IngredientsExtra = "ingredients";

    /// <summary>The step lines as a JSON array of strings, numbering removed, in card order.</summary>
    public const string StepsExtra = "steps";

    public const string NameExtra = "name";
    public const string PhoneExtra = "phone";
    public const string EmailExtra = "email";

    /// <summary>
    /// A card as plain strings. An empty field is left out rather than sent blank: a blank
    /// extra and a missing one mean different things to the caller — one is "the card says
    /// nothing here", the other is "I filled this in with nothing".
    /// </summary>
    public static IReadOnlyDictionary<string, string> CardExtras(CardReading reading)
    {
        var extras = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(extras, NameExtra, reading.Name);
        Add(extras, PhoneExtra, reading.Phone);
        Add(extras, EmailExtra, reading.Email);
        return extras;
    }

    /// <summary>
    /// A statement, on <b>the contract Post already uses</b>. Plate becomes a second sender
    /// of the same payload rather than a second dialect of it — Index must not have to know
    /// which app spoke. Null when there is no balance: a fabricated one would reach Index
    /// and be believed, which is far worse than one the owner has to read themselves.
    ///
    /// Sender name is passed as <c>null</c> deliberately: statement OCR has no
    /// institution-name extraction built yet (per this file's own doc comment, this path has
    /// never been exercised against a real device). A future release that builds OCR'd
    /// sender-name extraction from a scanned statement's header can populate this without
    /// another contract change — the field already exists on the wire.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? StatementExtras(MoneyFacts facts) =>
        IndexHandoff.PayloadFor(facts, senderName: null) is { } payload ? IndexHandoff.Extras(payload) : null;

    private static readonly JsonSerializerOptions WordsOnTheWire = new()
    {
        // A half stays a half in the text rather than becoming an escape; either reads back the same.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// A recipe as plain strings and nothing else: a title, and the ingredient and step lines each as a JSON array of
    /// strings. <b>No image, no file and no URI ever crosses</b> — the photograph is not kept by Plate and does not
    /// leave it. An empty title is left out rather than sent blank. Null when there is neither an ingredient nor a step:
    /// a title alone is not a recipe, and sending one would only give the owner something to throw away.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? RecipeExtras(RecipeReading reading)
    {
        if (reading.Ingredients.Count == 0 && reading.Steps.Count == 0)
        {
            return null;
        }

        var extras = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(extras, TitleExtra, reading.Title);
        extras[IngredientsExtra] = JsonSerializer.Serialize(reading.Ingredients, WordsOnTheWire);
        extras[StepsExtra] = JsonSerializer.Serialize(reading.Steps, WordsOnTheWire);
        return extras;
    }

    private static void Add(Dictionary<string, string> extras, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            extras[key] = value;
        }
    }
}
