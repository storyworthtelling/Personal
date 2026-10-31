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

    /// <summary>The book's seven timed steps, 60 minutes in all, with its asides and hand-offs.</summary>
    public static IReadOnlyList<RecipeClockStep> TimedSteps() =>
    [
        new("Oven to 425°F, rack in middle.", 5, Aside: "Kids’ noodle water on now."),
        new("Halve the potatoes. Toss with 2 tbsp oil, ½ tsp salt, pepper.", 10),
        new("Potatoes in, cut side down.", 5, Active: false, Aside: "In first: they need longer."),
        new("Pat the chicken dry. Rub with paprika, garlic, 1 tsp salt, oil.", 10),
        new("Chicken onto the pan, skin up, between the potatoes.", 20, Active: false, KidsHandOff: true),
        new("Chicken to 175°F at the thickest part.", 5, Aside: "Not there? Five more minutes."),
        new("Rest 5 min; lemon, parsley.", 5, Active: false),
    ];

    public static RecipeKidsMeal Kids() =>
        new("Buttered noodles", "noodles", ["Robin", "Juniper"], "Same table, same time. The chicken has paprika they don’t like.",
        [
            new("Main", "6 oz", "small pasta shapes"),
            new("Main", "1 tbsp", "butter"),
            new("Main", "½ cup", "frozen peas"),
            new("Main", "2 tbsp", "parmesan"),
            new("Main", "1", "carrot, in sticks"),
        ],
        [
            new("Pot of salted water on.", 40, Active: false),
            new("Pasta in. Peas in for the last 2 minutes.", 12),
            new("Drain. Butter, parmesan.", 8),
        ],
        "Dinner, with carrot sticks.");

    /// <summary>The chicken as the whole document: ingredients, timed steps, the kids' page, the rating page still to come.</summary>
    public static RecipeDocumentModel Timed(
        IReadOnlyList<RecipeClockStep>? steps = null,
        RecipeKidsMeal? kids = null,
        bool noKids = false,
        DateTime? notBefore = null,
        IReadOnlyList<RecipeIngredient>? ingredients = null,
        IReadOnlyList<string>? extraEquipment = null) =>
        Chicken(ingredients: ingredients, extra: extraEquipment) with
        {
            StepTexts = [], Steps = steps ?? TimedSteps(), KidsMeal = noKids ? null : kids ?? Kids(), NotBefore = notBefore, Pages = 1, KidsDinnerPage = null,
        };

    public static readonly DateTime Wednesday = new(2026, 11, 18, 0, 0, 0);

    /// <summary>The slow-cooker card: two morning steps, two evening ones.</summary>
    public static RecipeDocumentModel SlowCooker() =>
        new("Black bean chili", "American", "Slow cooker · family card", null, "Sam",
            Wednesday, Wednesday.AddHours(18).AddMinutes(15), Wednesday.AddHours(7).AddMinutes(30), 540, null, 6,
            new RecipeHeadcount(2, 2),
            [
                new("Dry", "1 lb", "black beans, soaked"),
                new("Dry", "1 tbsp", "chili powder"),
                new("Fresh", "1", "onion, diced"),
                new("Fresh", "2 tbsp", "scallions, sliced"),
            ],
            [], [], 1, null,
            [
                new("Stir. Taste for salt.", 15),
                new("Onion and garlic chopped; in with beans, tomatoes, spices.", 10, Aside: "Ten minutes, before school.", Morning: true),
                new("Toppings out: cheese, yogurt, scallions.", 10),
                new("Lid on. Low.", 10, Active: false, Morning: true, CalendarEvent: "Start the slow cooker"),
            ]);

    public static InkPageLayout Page(RecipeDocumentModel? model = null) => RecipeDocument.Build(model ?? Chicken()).Single();
}
