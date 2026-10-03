using Index.Domain.Model;

namespace Index.Domain.Worksheet;

/// <summary>
/// What one monthly worksheet cycle did — the quiet status sentence <c>SettingsStore</c> shows on the
/// non-admin Settings panel, the same idiom as <c>LastRunSummary</c> for the balance refresh (D89: the
/// round-trip itself is silent to the owner; this is the one place a failure becomes visible instead of
/// a mystery, not a notification).
/// </summary>
public sealed record WorksheetDeliveryResult(
    bool Ran,
    string? TargetYearMonth,
    bool? Delivered,
    int? LimitsApplied,
    int? LimitsMarkedUnreadable,
    int? CategoriesCreated)
{
    public static readonly WorksheetDeliveryResult NotDue = new(false, null, null, null, null, null);

    public string Summarize() => this switch
    {
        { Ran: false } => "Not due today",
        { Delivered: true, TargetYearMonth: { } month } =>
            $"Delivered {BudgetPeriod.DisplayMonthName(month)}’s worksheet{ReadBackClause()}",
        { Delivered: false } => $"Worksheet delivery failed{ReadBackClause()}",
        _ => "Worksheet cycle ran",
    };

    private string ReadBackClause()
    {
        if (LimitsApplied is null)
        {
            return string.Empty;
        }

        var parts = new List<string> { $"read back {LimitsApplied} limit(s)" };
        if (LimitsMarkedUnreadable is > 0)
        {
            parts.Add($"{LimitsMarkedUnreadable} unreadable");
        }
        if (CategoriesCreated is > 0)
        {
            parts.Add($"{CategoriesCreated} new categor{(CategoriesCreated == 1 ? "y" : "ies")}");
        }
        return " — " + string.Join(", ", parts);
    }
}
