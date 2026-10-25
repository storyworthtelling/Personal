namespace Constitution.Domain.Groceries;

/// <summary>When two unit words are one unit, and so two amounts can be added. There is no conversion between units: a cup and a tablespoon stay two lines.</summary>
internal static class Units
{
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
    {
        ["teaspoon"] = "tsp",
        ["tablespoon"] = "tbsp",
        ["tbs"] = "tbsp",
        ["tbl"] = "tbsp",
        ["ounce"] = "oz",
        ["pound"] = "lb",
        ["lbs"] = "lb",
    };

    /// <summary>The unit's key: lower case, no full stop, singular, abbreviations joined. Empty for no unit.</summary>
    public static string Key(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return string.Empty;
        }

        var key = string.Join(' ', IngredientNames.Words(unit.Trim().TrimEnd('.')));
        return Synonyms.GetValueOrDefault(key, key);
    }
}
