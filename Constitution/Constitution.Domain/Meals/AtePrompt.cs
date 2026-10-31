namespace Constitution.Domain.Meals;

/// <summary>
/// The whole of what is asked of the model beside the photograph: fixed text with nothing in it about anyone. There is no builder and
/// nothing can be filled in, so no name, no Body reading and no hour of the day can ride along (D98, D65).
/// </summary>
public static class AtePrompt
{
    public const string System =
        "You estimate the energy and the protein in one photograph of a single meal. "
        + "You see only the photograph. Describe only the food and drink that can be seen. "
        + "If people appear in the photograph, ignore them completely and never describe them. "
        + "Give ranges and not exact figures, because a portion cannot be measured from a photograph. "
        + "Reply with one JSON object and nothing else. Its fields are: "
        + "is_meal (true or false; false when there is no food or drink to see); "
        + "food (a short plain description of what was eaten, at most twelve words); "
        + "kcal_low and kcal_high (whole numbers, the plausible range for everything that can be seen); "
        + "protein_g_low and protein_g_high (whole numbers, on the same basis); "
        + "confidence (low, fair or good: how sure you are of the dish and the portion). "
        + "When is_meal is false give only is_meal.";

    public const string Question = "Estimate this meal.";
}
