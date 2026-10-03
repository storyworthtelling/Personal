namespace Index.Domain.Household;

/// <summary>
/// The words on her install's History and Growth sides when the household file cannot supply a chart
/// (remediation 3.6). Plain sentences, in the voice of the other empty pages — what is missing and why,
/// never an error code. Kept as constants so a test can hold the pages to them.
/// </summary>
public static class HouseholdChartCopy
{
    /// <summary>A version 1 file, published before the series existed: said ONLY in that case.</summary>
    public const string SnowballNotInSummary = "History isn\u2019t part of this summary yet.";

    /// <summary>A version 1 file: said ONLY in that case.</summary>
    public const string GrowthNotInSummary = "Growth isn\u2019t part of this summary yet.";

    /// <summary>The shared file was written by a newer Index than the one reading it. Reading it anyway
    /// could draw the wrong thing from a shape this build does not know, so nothing is drawn.</summary>
    public const string NewerFile =
        "The shared file was written by a newer Index than this one. Update Index on this phone to read it.";
}
