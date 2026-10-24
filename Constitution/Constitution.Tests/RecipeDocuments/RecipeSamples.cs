using Constitution.Domain.Documents;

namespace Constitution.Tests.RecipeDocuments;

/// <summary>Invented recipe pages shaped like book p.8. Every dish, name and quantity here is made up.</summary>
internal static class RecipeSamples
{
    public static readonly DateTime Tuesday = new(2026, 11, 17, 0, 0, 0);

    /// <summary>The card makes 6; tonight is 4, so the printed quantities are the book's.</summary>
    public static IReadOnlyList<RecipeIngredient> Ingredients() =>
    [
        new("Chicken", "3 lb", "bone-in chicken thighs (6)"),
        new("Chicken", "1½ tbsp", "smoked paprika"),
        new("Chicken", "1½ tsp", "garlic powder"),
        new("Chicken", "2¼ tsp", "salt, divided"),
        new("Potatoes", "3 lb", "small yellow potatoes, halved"),
        new("Potatoes", "4½ tbsp", "olive oil, divided"),
        new("Potatoes", "¾ tsp", "black pepper"),
        new("To finish", "1½", "lemon, in wedges"),
        new("To finish", "3 tbsp", "parsley, chopped"),
    ];

    public static string[] Steps() =>
    [
        "Oven to 425°F, rack in middle.",
        "Halve the potatoes. Toss with 2 tbsp oil, ½ tsp salt, pepper.",
        "Potatoes onto the sheet pan, cut side down.",
        "Pat the chicken dry. Rub with paprika, garlic, 1 tsp salt, oil.",
        "Chicken to 175°F at the thickest part, checked with a thermometer.",
    ];

    public static RecipeDocumentModel Chicken(
        IReadOnlyList<RecipeIngredient>? ingredients = null,
        RecipeHeadcount? headcount = null,
        int cardServings = 6,
        int? kidsPage = 3,
        string? withLine = "With potatoes",
        int? handsOn = 20,
        IReadOnlyList<string>? extra = null) =>
        new(
            "Sheet-pan chicken", "American", "From the recipe box · family card", withLine, "Lee",
            Tuesday, Tuesday.AddHours(18).AddMinutes(15), Tuesday.AddHours(17).AddMinutes(15), 60, handsOn, cardServings,
            headcount ?? new RecipeHeadcount(2, 2), ingredients ?? Ingredients(), Steps(), extra ?? [], 4, kidsPage);

    public static InkPageLayout Page(RecipeDocumentModel? model = null) => RecipeDocument.Build(model ?? Chicken()).Single();
}
