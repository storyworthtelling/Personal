using Constitution.Domain.Household.Records;

namespace Constitution.Domain.Recipes;

/// <summary>
/// The cuisines the recipe box files a card under (D53, D60): a closed list with <see cref="Other"/> as
/// the catch-all, so a card always has a pile. Growing the list is a deliberate edit to
/// <c>CuisineTests</c>; the enum names are code, and the words a record stores are in
/// <see cref="Cuisines.Words"/>.
/// </summary>
public enum Cuisine
{
    American,
    Asian,
    French,
    Indian,
    Italian,
    Mediterranean,
    Mexican,

    /// <summary>The catch-all: anything the list above does not name. Its pile is always last.</summary>
    Other,
}

/// <summary>
/// The stored words and the strip labels for <see cref="Cuisine"/>. <b>The words are frozen once
/// shipped</b>: the household file outlives every build that reads it, so each is a literal, never an
/// enum name or <c>nameof</c>, which would rename the stored value with the identifier.
/// </summary>
public static class Cuisines
{
    public static FieldWords<Cuisine> Words { get; } = new(new Dictionary<Cuisine, string>
    {
        [Cuisine.American] = "american",
        [Cuisine.Asian] = "asian",
        [Cuisine.French] = "french",
        [Cuisine.Indian] = "indian",
        [Cuisine.Italian] = "italian",
        [Cuisine.Mediterranean] = "mediterranean",
        [Cuisine.Mexican] = "mexican",
        [Cuisine.Other] = "other",
    });

    /// <summary>Every cuisine in the order the action card offers them: by name, the catch-all last.</summary>
    public static IReadOnlyList<Cuisine> All { get; } =
    [
        Cuisine.American, Cuisine.Asian, Cuisine.French, Cuisine.Indian,
        Cuisine.Italian, Cuisine.Mediterranean, Cuisine.Mexican, Cuisine.Other,
    ];

    /// <summary>The name on a pile's label strip.</summary>
    public static string Label(Cuisine cuisine) => cuisine switch
    {
        Cuisine.American => "American",
        Cuisine.Asian => "Asian",
        Cuisine.French => "French",
        Cuisine.Indian => "Indian",
        Cuisine.Italian => "Italian",
        Cuisine.Mediterranean => "Mediterranean",
        Cuisine.Mexican => "Mexican",
        Cuisine.Other => "Other",
        _ => throw new ArgumentOutOfRangeException("cuisine", "No label for this cuisine."),
    };
}
