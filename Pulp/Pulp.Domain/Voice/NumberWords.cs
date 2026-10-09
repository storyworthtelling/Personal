namespace Pulp.Domain.Voice;

/// <summary>Small counts in words, so a voiced line says "three", never "3".</summary>
internal static class NumberWords
{
    private static readonly string[] Words =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen", "twenty",
    ];

    public static string Of(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return count < Words.Length ? Words[count] : "more than twenty";
    }
}
