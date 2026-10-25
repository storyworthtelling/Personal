namespace Constitution.Domain.Meals;

/// <summary>How sure the reading is of the dish and the portion, said in the three words the card uses.</summary>
public enum AteConfidence
{
    Low,
    Fair,
    Good,
}

/// <summary>
/// What one photograph came to: a short description and a range of energy (and, when it was plausible, of protein), never a point.
/// The only way to make one is <see cref="TryCreate"/>, which refuses anything outside what a single meal can be, so a zero, a
/// negative or an absurd figure cannot be held, shown or stored. It holds words and whole numbers and never the picture.
/// </summary>
public sealed record AteReading
{
    public const int MinKcal = 50;
    public const int MaxKcal = 4000;
    public const int MaxSpread = 4;
    public const int MaxProtein = 250;
    public const int LongestWhat = 80;

    private AteReading(string what, int kcalLow, int kcalHigh, int? proteinLow, int? proteinHigh, AteConfidence confidence)
    {
        What = what;
        KcalLow = kcalLow;
        KcalHigh = kcalHigh;
        ProteinLow = proteinLow;
        ProteinHigh = proteinHigh;
        Confidence = confidence;
    }

    public string What { get; }

    public int KcalLow { get; }

    public int KcalHigh { get; }

    public int? ProteinLow { get; }

    public int? ProteinHigh { get; }

    public AteConfidence Confidence { get; }

    /// <summary>The figure the card prints: the middle of the range, to the nearest fifty.</summary>
    public int KcalAbout => (int)(Math.Round((KcalLow + KcalHigh) / 2.0 / 50.0, MidpointRounding.AwayFromZero) * 50.0);

    /// <summary>
    /// A reading, or false when the figures are not those of a meal: energy outside 50 to 4,000, a low above its high, or a spread wider
    /// than four to one. A protein range that is not plausible is dropped and the energy kept. The description is made one plain line.
    /// </summary>
    public static bool TryCreate(
        string? what,
        long kcalLow,
        long kcalHigh,
        long? proteinLow,
        long? proteinHigh,
        AteConfidence confidence,
        out AteReading? reading)
    {
        reading = null;
        var line = OneLine(what);
        if (line.Length == 0 || kcalLow < MinKcal || kcalHigh > MaxKcal || kcalLow > kcalHigh || kcalHigh > kcalLow * MaxSpread)
        {
            return false;
        }

        int? lowProtein = null;
        int? highProtein = null;
        if (proteinLow is { } low && proteinHigh is { } high && low >= 0 && high <= MaxProtein && low <= high && high <= Math.Max(low, 5) * MaxSpread * 2)
        {
            lowProtein = (int)low;
            highProtein = (int)high;
        }

        reading = new AteReading(line, (int)kcalLow, (int)kcalHigh, lowProtein, highProtein, confidence);
        return true;
    }

    private static string OneLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var flat = new string(text.Select(letter => char.IsControl(letter) ? ' ' : letter).ToArray());
        var single = string.Join(' ', flat.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return single.Length > LongestWhat ? single[..LongestWhat].TrimEnd() : single;
    }
}

/// <summary>A reading attached to the day and the meal it was for. This is all that is kept; there is no person on it (D65).</summary>
public sealed record AteEstimate(DateOnly Day, AteMeal Meal, AteReading Reading);
