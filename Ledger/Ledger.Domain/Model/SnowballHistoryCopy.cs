using System.Globalization;

namespace Index.Domain.Model;

/// <summary>
/// The words on Snowball's History side (mockup 6), kept as pure functions so a test can hold them to
/// the mockup rather than a page being read for them.
/// </summary>
public static class SnowballHistoryCopy
{
    /// <summary>Mockup 6 draws no empty state, so this is a plain, neutral sentence in the voice of the
    /// other empty pages (Budget's, Savings'): what is missing and when it will not be.</summary>
    public const string EmptyHistory = "No history yet. Index records each loan's balance once a week, and these charts begin with the first.";

    public const string EmptyLoans = "No loans to plan yet. A loan with an APR and a weekly payment appears here, in payoff order.";

    private const string FootnoteTail = "each panel runs from $0 to that loan’s own peak";

    /// <summary>"May 2025 to now · each panel runs from $0 to that loan’s own peak" — the mockup's text
    /// with the real first recorded month. With no weeks there is no first month to name, so the
    /// prefix is dropped rather than invented.</summary>
    public static string Footnote(DateOnly? firstWeek) =>
        firstWeek is { } first
            ? $"{first.ToString("MMMM yyyy", CultureInfo.InvariantCulture)} to now · {FootnoteTail}"
            : "Each panel runs from $0 to that loan’s own peak.";

    /// <summary>The date half of the caption a touch produces: "Sep 25, 2026".</summary>
    public static string WeekCaption(DateOnly week) => week.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>"Maple Ridge Mortgage (mortgage)": the panel title names a mortgage as one. What makes a loan a
    /// mortgage is <see cref="AccountStatus.IsMortgage"/>, never its name.</summary>
    public static string PanelName(string name, bool isMortgage) => isMortgage ? $"{name} (mortgage)" : name;
}
