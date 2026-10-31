using Constitution.Domain.Recipes;
using Storyworthtelling.Household;

namespace Constitution.Tests.Recipes;

/// <summary>Invented dishes for the recipe tests. No real recipe, no real source, no copied text.</summary>
internal static class RecipeData
{
    public const string OwnerWriter = "owner-install";
    public const string PartnerWriter = "partner-install";

    public static HlcTimestamp At(long millis, int counter = 0) => new(millis, counter);

    public static Recipe Chili() => new(
        new RecipeId("black-bean-chili"),
        "Black bean chili",
        Cuisine.Mexican,
        [new Ingredient("2", "cans", "black beans"), new Ingredient("1", null, "onion"), new Ingredient(null, null, "salt")],
        [new RecipeStep("Soften the onion.", 10, true), new RecipeStep("Simmer everything together.", 35, false), new RecipeStep("Taste and salt.", null, true)],
        6,
        RecipeSource.Typed,
        forKids: true);

    public static Recipe Plain(string id, string title, Cuisine cuisine, params string[] ingredientNames) => new(
        new RecipeId(id),
        title,
        cuisine,
        ingredientNames.Select(name => new Ingredient("1", "cup", name)).ToList(),
        [],
        null,
        null,
        forKids: false);

    /// <summary>Four piles: Mexican 3, Italian 3, Asian 2, American 1, Other 1.</summary>
    public static List<Recipe> Box() =>
    [
        Plain("black-bean-chili", "Black bean chili", Cuisine.Mexican, "black beans", "onion"),
        Plain("chicken-tinga-tacos", "Chicken tinga tacos", Cuisine.Mexican, "chicken", "chipotle", "tortillas"),
        Plain("sheet-pan-fajitas", "Sheet-pan fajitas", Cuisine.Mexican, "peppers", "jalapeño"),
        Plain("turkey-meatballs", "Turkey meatballs, spaghetti", Cuisine.Italian, "turkey", "spaghetti"),
        Plain("weeknight-lasagna", "Weeknight lasagna", Cuisine.Italian, "pasta sheets", "ricotta"),
        Plain("lemon-orzo", "Lemon orzo with peas", Cuisine.Italian, "orzo", "peas", "lemon"),
        Plain("veggie-fried-rice", "Veggie fried rice", Cuisine.Asian, "rice", "peas"),
        Plain("peanut-noodles", "Peanut noodles", Cuisine.Asian, "noodles", "peanut butter"),
        Plain("sheet-pan-chicken", "Sheet-pan chicken, potatoes", Cuisine.American, "chicken", "potatoes"),
        Plain("lentil-soup", "Lentil soup", Cuisine.Other, "lentils", "carrots"),
    ];

    public static HouseholdDocument Document(IEnumerable<Recipe> recipes)
    {
        var document = HouseholdDocument.Empty;
        var tick = 1000L;
        foreach (var recipe in recipes)
        {
            document = Constitution.Domain.Household.Records.HouseholdRecords.Put(
                document, Constitution.Domain.Household.Records.RecipeMaps.Recipe, recipe, At(tick++), OwnerWriter);
        }

        return document;
    }
}
