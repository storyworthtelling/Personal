namespace Constitution.Domain.Body;

/// <summary>Conversions into Body's canonical units. The platform speaks grams and seconds; the charts speak pounds and hours (D22).</summary>
public static class BodyUnits
{
    private const double GramsPerPound = 453.59237;
    private const double SecondsPerHour = 3600;

    /// <summary>Pounds in <paramref name="grams"/>.</summary>
    public static double PoundsFromGrams(double grams) => grams / GramsPerPound;

    /// <summary>Hours in <paramref name="seconds"/>.</summary>
    public static double HoursFromSeconds(double seconds) => seconds / SecondsPerHour;
}
