namespace Index.Domain.Voice;

/// <summary>
/// Whole numbers up to ninety-nine as the words a person would write ("twelve", "twenty-one"). The seats'
/// facts are handed to the model in words so that nothing in a prompt that is not a rate is a digit — which
/// is what lets a test say "no figure" by looking for any digit at all. A hundred or more falls back to
/// digits rather than inventing a spelling nobody asked for.
/// </summary>
public static class NumberWords
{
    private static readonly string[] Units =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
        "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    ];

    private static readonly string[] Tens =
        ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    public static string Of(int number)
    {
        if (number is < 0 or > 99)
        {
            return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (number < 20)
        {
            return Units[number];
        }

        var tens = Tens[number / 10];
        return number % 10 == 0 ? tens : $"{tens}-{Units[number % 10]}";
    }

    /// <summary>"One loan" / "Three loans" — the count a sentence starts with.</summary>
    public static string Capitalised(int number)
    {
        var words = Of(number);
        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}
