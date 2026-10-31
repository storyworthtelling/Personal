using Constitution.Domain.Household.Records;

namespace Constitution.Domain.Groceries;

/// <summary>
/// The store sections a grocery list is grouped under: a closed list, in the order the shop is walked. The enum names are code;
/// the words a record stores are in <see cref="Aisles.Words"/> and are frozen once shipped.
/// </summary>
public enum Aisle
{
    Produce,
    MeatAndFish,
    DairyAndEggs,
    Bakery,
    Frozen,
    CannedAndJarred,
    DryGoods,
    BakingAndSpices,

    /// <summary>The catch-all: an ingredient the keyword table does not place. Last in the shop, so nothing is ever unplaced.</summary>
    Other,
}

public static class Aisles
{
    public static FieldWords<Aisle> Words { get; } = new(new Dictionary<Aisle, string>
    {
        [Aisle.Produce] = "produce",
        [Aisle.MeatAndFish] = "meat-fish",
        [Aisle.DairyAndEggs] = "dairy-eggs",
        [Aisle.Bakery] = "bakery",
        [Aisle.Frozen] = "frozen",
        [Aisle.CannedAndJarred] = "canned-jarred",
        [Aisle.DryGoods] = "dry-goods",
        [Aisle.BakingAndSpices] = "baking-spices",
        [Aisle.Other] = "other",
    });

    /// <summary>Every aisle in shopping order.</summary>
    public static IReadOnlyList<Aisle> All { get; } =
    [
        Aisle.Produce, Aisle.MeatAndFish, Aisle.DairyAndEggs, Aisle.Bakery, Aisle.Frozen,
        Aisle.CannedAndJarred, Aisle.DryGoods, Aisle.BakingAndSpices, Aisle.Other,
    ];

    /// <summary>The heading over a section of the list.</summary>
    public static string Label(Aisle aisle) => aisle switch
    {
        Aisle.Produce => "Produce",
        Aisle.MeatAndFish => "Meat and fish",
        Aisle.DairyAndEggs => "Dairy and eggs",
        Aisle.Bakery => "Bakery",
        Aisle.Frozen => "Frozen",
        Aisle.CannedAndJarred => "Canned and jarred",
        Aisle.DryGoods => "Dry goods",
        Aisle.BakingAndSpices => "Baking and spices",
        Aisle.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(aisle), aisle, "No label for this aisle."),
    };

    /// <summary>Whether what is bought here spoils if the dinner it was bought for is not cooked.</summary>
    public static bool IsPerishable(Aisle aisle) => aisle is Aisle.Produce or Aisle.MeatAndFish or Aisle.DairyAndEggs or Aisle.Bakery;

    // Checked top to bottom, and the first aisle with a phrase in the name wins, so the specific comes before the general:
    // "coconut milk" is canned before "milk" is dairy, "bell pepper" is produce before "pepper" is a spice. Phrases are in
    // key form (singular words) and match whole words only, so "butter" does not find "butternut".
    private static readonly (Aisle Aisle, string[] Phrases)[] Table =
    [
        (Aisle.Frozen, ["frozen"]),
        (Aisle.CannedAndJarred,
        [
            "canned", "crushed tomato", "diced tomato", "tomato paste", "tomato sauce", "coconut milk", "peanut butter", "almond butter",
            "broth", "stock", "black bean", "kidney bean", "white bean", "pinto bean", "chickpea", "salsa", "jarred", "pickle",
            "green olive", "black olive",
        ]),
        (Aisle.Produce, ["bell pepper", "chili pepper", "hot pepper", "green onion", "jalapeno"]),
        (Aisle.MeatAndFish, ["chicken", "beef", "pork", "turkey", "sausage", "bacon", "ham", "salmon", "shrimp", "cod", "fish", "lamb", "steak"]),
        (Aisle.DairyAndEggs, ["milk", "cheese", "parmesan", "mozzarella", "cheddar", "butter", "yogurt", "cream", "egg"]),
        (Aisle.DryGoods, ["breadcrumb", "panko", "rice", "pasta", "spaghetti", "penne", "noodle", "lentil", "oat", "quinoa", "couscous"]),
        (Aisle.Bakery, ["bread", "bun", "tortilla", "pita", "bagel", "roll"]),
        (Aisle.Produce,
        [
            "onion", "garlic", "lemon", "lime", "spinach", "carrot", "potato", "tomato", "celery", "lettuce", "cucumber", "mushroom",
            "broccoli", "cilantro", "parsley", "basil", "apple", "banana", "squash", "zucchini", "ginger", "kale", "cabbage", "avocado",
            "scallion",
        ]),
        (Aisle.BakingAndSpices,
        [
            "flour", "sugar", "salt", "pepper", "cumin", "paprika", "oregano", "thyme", "cinnamon", "vanilla", "baking", "oil", "vinegar",
            "honey", "spice", "cayenne", "chili flake", "soy sauce", "cornstarch", "yeast", "syrup",
        ]),
    ];

    /// <summary>The aisle an ingredient is looked for in, by the words of its name; <see cref="Aisle.Other"/> when none is known.</summary>
    public static Aisle Of(string ingredientName)
    {
        ArgumentNullException.ThrowIfNull(ingredientName);
        var words = IngredientNames.Words(ingredientName);
        foreach (var (aisle, phrases) in Table)
        {
            if (phrases.Any(phrase => Contains(words, phrase.Split(' '))))
            {
                return aisle;
            }
        }

        return Aisle.Other;
    }

    private static bool Contains(IReadOnlyList<string> words, string[] phrase)
    {
        for (var start = 0; start + phrase.Length <= words.Count; start++)
        {
            var match = true;
            for (var offset = 0; offset < phrase.Length && match; offset++)
            {
                match = words[start + offset] == phrase[offset];
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
