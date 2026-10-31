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
/// <param name="Steps">
/// The timed steps (book p.8). When present the document numbers its own pages: ingredients, steps, the kids' page when
/// <paramref name="KidsMeal"/> is present, then <paramref name="TrailingPages"/>; <paramref name="Pages"/> and
/// <paramref name="KidsDinnerPage"/> are read only when there are no timed steps.
/// </param>
/// <param name="KidsMeal">The children's different dinner on nights they eat differently, else null.</param>
/// <param name="NotBefore">The earliest a step may start (the moment the cook is handed the page); null for no limit.</param>
/// <param name="TrailingPages">Pages that come after these and are built elsewhere (the rating page), counted in "x of N".</param>
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
    int? KidsDinnerPage = null,
    IReadOnlyList<RecipeClockStep>? Steps = null,
    RecipeKidsMeal? KidsMeal = null,
    DateTime? NotBefore = null,
    int TrailingPages = 1);

/// <summary>
/// One step with the time it takes, from the card. <paramref name="Minutes"/> is how long until the next step is due;
/// <paramref name="Active"/> is whether the cook is busy for it (chopping) or waiting (baking). A <paramref name="Morning"/>
/// step of a slow-cooker meal runs forward from the morning start; every other step is counted back from dinner.
/// </summary>
/// <param name="Aside">One line under the step: the why, or a warning.</param>
/// <param name="CalendarEvent">The title of the calendar event the planner placed for this step, named so paper and calendar agree.</param>
/// <param name="KidsHandOff">Points the cook at the children's page ("Kids' noodles: page 3.").</param>
public sealed record RecipeClockStep(
    string Text,
    int Minutes,
    bool Active = true,
    string? Aside = null,
    bool Morning = false,
    string? CalendarEvent = null,
    bool KidsHandOff = false);

/// <summary>
/// The children's dinner on a night they eat differently (book p.9): one line saying why, its own short ingredient list
/// and steps timed to the same dinner. It has no member that could hold a nutrition figure: the children's meals are
/// balanced but not counted.
/// </summary>
/// <param name="ShortName">How the steps page refers to it: "noodles" in "Kids' noodles: page 3."</param>
/// <param name="Children">Who it is for, in the words printed ("Robin", "Juniper").</param>
/// <param name="WhyLine">Why tonight is different.</param>
/// <param name="DinnerLine">The final row's words: "Dinner, with carrot sticks."</param>
public sealed record RecipeKidsMeal(
    string Title,
    string ShortName,
    IReadOnlyList<string> Children,
    string WhyLine,
    IReadOnlyList<RecipeIngredient> Ingredients,
    IReadOnlyList<RecipeClockStep> Steps,
    string DinnerLine = "Dinner.");
