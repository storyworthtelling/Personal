using System.Text.RegularExpressions;

namespace Codex.Domain.Plate;

/// <summary>What a card gave up. Any field may be empty; none of them may be a guess.</summary>
public sealed record CardReading(string Name, string Phone, string Email);

/// <summary>
/// Reading a business card out of recognised text.
///
/// <para>Plate is <b>asked</b> to look — by Post's Rolodex — and returns what it found. It
/// never decides what a piece of paper is for; the app that wants the data already knows
/// what it wants, which is what makes this the simpler half of the contract.</para>
///
/// <para><b>It errs toward leaving a field empty.</b> The owner corrects the result before
/// it goes back, and a wrong phone number that looks right is worse than a blank one,
/// because nobody re-reads a field that is already filled.</para>
/// </summary>
public static partial class BusinessCard
{
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailLike();

    [GeneratedRegex(@"^(https?://|www\.)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlLike();

    // What a card puts under the name and what a person is called are different, and the
    // second is the only one being asked for.
    [GeneratedRegex(
        @"\b(suite|ste|floor|fl|street|st|road|rd|avenue|ave|drive|dr|lane|ln|way|blvd|"
        + @"director|manager|principal|partner|officer|president|founder|engineer|surveyor|"
        + @"consultant|associate|analyst|designer|developer|architect|attorney|agent|"
        + @"ltd|llc|inc|corp|co|gmbh|plc)\b\.?",
        RegexOptions.IgnoreCase)]
    private static partial Regex NotAName();

    /// <summary>The shortest thing anyone can actually dial.</summary>
    private const int DiallableDigits = 10;

    public static CardReading Read(IReadOnlyList<string> lines)
    {
        var trimmed = lines is null
            ? []
            : lines.Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList();

        var email = trimmed
            .Select(line => EmailLike().Match(line))
            .FirstOrDefault(match => match.Success)?.Value ?? string.Empty;

        var phoneLine = trimmed.FirstOrDefault(line =>
            line.Count(char.IsDigit) >= DiallableDigits && !EmailLike().IsMatch(line));

        var name = trimmed.FirstOrDefault(line =>
            line != phoneLine
            && !EmailLike().IsMatch(line)
            && !UrlLike().IsMatch(line)
            && !NotAName().IsMatch(line)
            // A card almost always leads with the firm, and firms are set in capitals.
            // Taking that as the name is the single most likely wrong answer, and it looks
            // right enough that nobody would fix it.
            && !IsShouted(line)
            // Two to four words: one is a logo word, five is a sentence.
            && WordCount(line) is >= 2 and <= 4);

        return new CardReading(name ?? string.Empty, phoneLine ?? string.Empty, email);
    }

    private static bool IsShouted(string line)
    {
        var letters = line.Where(char.IsLetter).ToList();
        return letters.Count > 0 && letters.All(char.IsUpper);
    }

    private static int WordCount(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
