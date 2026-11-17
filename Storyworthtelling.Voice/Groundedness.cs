using System.Text.RegularExpressions;

namespace Storyworthtelling.Voice;

/// <summary>
/// The facts check (audit W10.5): what a reply says that its request never gave it — a number (in digits or words),
/// a name (a capitalised word inside a sentence) or a claim (finished, complete, delivered, …). The messenger logs
/// <c>VOICE UNGROUNDED &lt;seat&gt;</c> when there is any, so a drift is found in the weekly logs; the line is still
/// shown, and the log never carries the reply or the facts. A cheap lexical check, not a judge: it can be fooled by a
/// paraphrase, and it errs toward flagging.
/// </summary>
public static partial class Groundedness
{
    private static readonly HashSet<string> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
        "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty", "thirty",
        "forty", "fifty", "sixty", "seventy", "eighty", "ninety", "hundred", "thousand", "million", "half", "dozen",
    };

    private static readonly HashSet<string> ClaimWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "finished", "complete", "completed", "done", "delivered", "record", "best", "worst", "never", "always",
    };

    /// <summary>The reply's words its facts do not contain, in the order they appear; empty when it is grounded.</summary>
    public static IReadOnlyList<string> Ungrounded(string reply, string facts)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(facts);
        var given = new HashSet<string>(Words().Matches(facts).Select(match => match.Value), StringComparer.OrdinalIgnoreCase);
        var found = new List<string>();

        foreach (Match number in Digits().Matches(reply))
        {
            if (!facts.Contains(number.Value, StringComparison.Ordinal))
            {
                found.Add(number.Value);
            }
        }

        foreach (var sentence in Sentences().Split(reply))
        {
            var words = Words().Matches(sentence).Select(match => match.Value).ToList();
            for (var index = 0; index < words.Count; index++)
            {
                var word = words[index];
                if (given.Contains(word))
                {
                    continue;
                }

                var isName = index > 0 && char.IsUpper(word[0]) && word != "I";
                if (NumberWords.Contains(word) || ClaimWords.Contains(word) || isName)
                {
                    found.Add(word);
                }
            }
        }

        return found;
    }

    [GeneratedRegex(@"\d+(?:[.,:]\d+)*")]
    private static partial Regex Digits();

    [GeneratedRegex(@"\p{L}[\p{L}'’]*")]
    private static partial Regex Words();

    [GeneratedRegex(@"(?<=[.!?;:—])\s+")]
    private static partial Regex Sentences();
}
