namespace Constitution.Domain.Meals;

/// <summary>
/// How an ingredient line and a taste word are compared: lower case, letters only, a plural read as its singular, and
/// the words that describe a preparation rather than a food left out. Both sides go through the same function, so
/// "Mushrooms" on a card and "mushroom" in a note meet.
/// </summary>
internal static class TasteTokens
{
    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "and", "the", "fresh", "dried", "large", "small", "whole", "can", "canned", "chopped", "diced", "sliced", "minced", "for", "with",
    };

    public static IReadOnlyList<string> Of(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var tokens = new List<string>();
        var word = new System.Text.StringBuilder();
        foreach (var letter in text.ToLowerInvariant() + " ")
        {
            if (char.IsLetter(letter))
            {
                word.Append(letter);
                continue;
            }

            if (word.Length > 0)
            {
                var token = Singular(word.ToString());
                if (token.Length >= 3 && !Stop.Contains(token))
                {
                    tokens.Add(token);
                }

                word.Clear();
            }
        }

        return tokens;
    }

    public static string Key(string? text) => string.Join(' ', Of(text));

    /// <summary>Whether <paramref name="needle"/> appears in <paramref name="haystack"/> as consecutive words.</summary>
    public static bool Contains(IReadOnlyList<string> haystack, IReadOnlyList<string> needle)
    {
        if (needle.Count == 0 || needle.Count > haystack.Count)
        {
            return false;
        }

        for (var start = 0; start + needle.Count <= haystack.Count; start++)
        {
            var match = true;
            for (var offset = 0; offset < needle.Count && match; offset++)
            {
                match = string.Equals(haystack[start + offset], needle[offset], StringComparison.Ordinal);
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private static string Singular(string word)
    {
        if (word.Length <= 3)
        {
            return word;
        }

        if (word.EndsWith("ies", StringComparison.Ordinal))
        {
            return word[..^3] + "y";
        }

        if (word.EndsWith("oes", StringComparison.Ordinal))
        {
            return word[..^2];
        }

        if (word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && !word.EndsWith("us", StringComparison.Ordinal) && !word.EndsWith("is", StringComparison.Ordinal))
        {
            return word[..^1];
        }

        return word;
    }
}
