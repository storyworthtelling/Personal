using System.Text.RegularExpressions;

namespace Constitution.Domain.Groceries;

/// <summary>How two spellings of an ingredient are decided to be one thing.</summary>
public static class IngredientNames
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// The comparison key of a name: lower case, single spaces, each word with a plural ending taken off ("onions" is "onion",
    /// "tomatoes" is "tomato", "berries" is "berry"). The same function is used on both sides of every match, so a word it
    /// mangles ("molasses") is mangled the same way everywhere and still matches itself.
    /// </summary>
    public static string Key(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return string.Join(' ', Words(name));
    }

    /// <summary>The key's words, one at a time.</summary>
    internal static IReadOnlyList<string> Words(string name)
    {
        var collapsed = Spaces.Replace(name.Trim().ToLowerInvariant(), " ");
        return collapsed.Length == 0 ? [] : collapsed.Split(' ').Select(Singular).ToList();
    }

    internal static string Singular(string word)
    {
        if (word.Length <= 3 || !word.EndsWith('s') || word.EndsWith("ss", StringComparison.Ordinal)
            || word.EndsWith("us", StringComparison.Ordinal) || word.EndsWith("is", StringComparison.Ordinal))
        {
            return word;
        }

        if (word.EndsWith("ies", StringComparison.Ordinal))
        {
            return word[..^3] + "y";
        }

        if (word.EndsWith("oes", StringComparison.Ordinal) || word.EndsWith("ches", StringComparison.Ordinal)
            || word.EndsWith("shes", StringComparison.Ordinal) || word.EndsWith("xes", StringComparison.Ordinal))
        {
            return word[..^2];
        }

        return word[..^1];
    }
}
