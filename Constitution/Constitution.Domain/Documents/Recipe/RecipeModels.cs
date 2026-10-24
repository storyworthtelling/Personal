namespace Constitution.Domain.Documents;

/// <summary>Who is eating tonight, guests from the calendar included. A child counts as one serving.</summary>
public sealed record RecipeHeadcount(int Adults, int Children, int Guests = 0)
{
    public int Total => Adults + Children + Guests;
}

/// <summary>
/// One ingredient line as the card has it: the heading it is grouped under (when it is used), the
/// quantity in the words the card wrote ("¾ cup", "1 1/2 tsp"), and the name.
/// </summary>
public sealed record RecipeIngredient(string Group, string Quantity, string Name);

/// <summary>
/// What page 1 of tonight's recipe needs (book p.8). The quantities are the card's own and are scaled
/// by the document from <paramref name="CardServings"/> to the headcount, so no caller does arithmetic.
/// </summary>
/// <param name="SourceLine">Where the card came from, as the kicker words it: "From the recipe box · family card".</param>
/// <param name="Cuisine">Carried for the later pages; the ingredients page does not draw it.</param>
/// <param name="WithLine">What it is served with ("With potatoes"), leading the headcount line, or null.</param>
/// <param name="Cook">Tonight's cook, in the footer.</param>
/// <param name="Day">The day, for the footer.</param>
/// <param name="DinnerAt">When dinner is on the table.</param>
/// <param name="StartAt">When the cook starts (the morning for a slow-cooker meal).</param>
/// <param name="TotalMinutes">How long the meal takes in all.</param>
/// <param name="HandsOnMinutes">How many of them the cook is busy for, or null to leave it out.</param>
/// <param name="StepTexts">The steps' words; the equipment list is derived from these and the ingredients.</param>
/// <param name="ExtraEquipment">Equipment the card names that no step mentions.</param>
/// <param name="Pages">How many pages the recipe document has, for "1 of N".</param>
/// <param name="KidsDinnerPage">The page of the children's dinner when they eat differently tonight, else null.</param>
public sealed record RecipeDocumentModel(
    string Title,
    string? Cuisine,
    string SourceLine,
    string? WithLine,
    string Cook,
    DateTime Day,
    DateTime DinnerAt,
    DateTime StartAt,
    int TotalMinutes,
    int? HandsOnMinutes,
    int CardServings,
    RecipeHeadcount Headcount,
    IReadOnlyList<RecipeIngredient> Ingredients,
    IReadOnlyList<string> StepTexts,
    IReadOnlyList<string> ExtraEquipment,
    int Pages,
    int? KidsDinnerPage = null);
