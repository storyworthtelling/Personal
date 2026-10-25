namespace Constitution.Domain.Recipes.Screens;

/// <summary>What came back from Plate's recipe read (C-6.5.1, D99).</summary>
public abstract record ScanReturn
{
    /// <summary>The owner backed out of Plate. Nothing happened and nothing is said.</summary>
    public sealed record Cancelled : ScanReturn;

    /// <summary>Plate answered with something that cannot be used. The sentence names the field and never the value.</summary>
    public sealed record Refused(string Sentence) : ScanReturn;

    /// <summary>The words on the card: a title, ingredient lines and step lines.</summary>
    public sealed record Read(RecipeReadPayload Payload) : ScanReturn;
}

/// <summary>Turning Plate's answer into one of the three outcomes. Pure, so the Android code only moves strings across.</summary>
public static class ScanReturns
{
    public const string NothingOnTheCardSentence = "Plate found no ingredients or steps on that card, so nothing was kept.";

    /// <summary>
    /// <paramref name="ok"/> is whether Plate returned a result at all. The three strings are its extras exactly as received; the
    /// parse is <see cref="RecipeReadContract.Parse"/> and nothing else, so what Constitution accepts is what the contract says.
    /// </summary>
    public static ScanReturn Decide(bool ok, string? title, string? ingredientsJson, string? stepsJson)
    {
        if (!ok)
        {
            return new ScanReturn.Cancelled();
        }

        var parsed = RecipeReadContract.Parse(title, ingredientsJson, stepsJson);
        if (parsed.Payload is { } payload)
        {
            return new ScanReturn.Read(payload);
        }

        return new ScanReturn.Refused(parsed.RefusedField == "recipe"
            ? NothingOnTheCardSentence
            : "Plate’s answer could not be used (" + parsed.RefusedField + "), so nothing was kept.");
    }
}

/// <summary>
/// The request Constitution makes of Plate: Plate's recipe activity, named directly, with the read action and nothing carried. The
/// words are the contract's; no extra, no data and no URI go out, because nothing needs to.
/// </summary>
public static class ScanRequest
{
    public const string Action = RecipeReadContract.Action;

    public const string Package = RecipeReadContract.PlatePackage;

    public const string Component = RecipeReadContract.Activity;

    /// <summary>Said when Plate is not on this phone or will not take the request.</summary>
    public const string PlateAbsentSentence = "Plate would not open, so the card was not scanned.";
}
