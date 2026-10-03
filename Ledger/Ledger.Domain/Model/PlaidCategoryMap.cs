namespace Index.Domain.Model;

public enum PlaidCategoryKind
{
    /// <summary>Plaid's category has no row here (or Plaid sent none): the transaction is spending, and
    /// lands in Uncategorized until a merchant rule or the owner places it.</summary>
    Unmapped,

    /// <summary>Plaid's category maps onto one of the app's spending categories.</summary>
    Category,

    /// <summary>Not spending at all: income, transfers, loan and card payments (D140).</summary>
    NotSpending,
}

public readonly record struct PlaidCategoryDecision(PlaidCategoryKind Kind, string? CategoryName);

/// <summary>
/// R8 / plan §2.4: Plaid's <c>personal_finance_category</c> mapped onto the owner's seeded spending
/// categories — the first category a transaction gets when no merchant rule has claimed it, and the
/// line between "spent" and "money merely moving" (D140: the debt payments aren't a line on Budget; the
/// loans and what they are paid live on Snowball).
///
/// <para><b>This table is the owner's to review.</b> It is deliberately short: a row is here only where
/// Plaid's label and the household's category mean the same thing nearly every time. Everything else is
/// left <see cref="PlaidCategoryKind.Unmapped"/> on purpose — an Uncategorized transaction is visible and
/// one tap (which also writes a merchant rule) settles it for good, while a confident wrong guess files
/// money under the wrong card and nothing shows it.</para>
///
/// <para>Precedence, applied by <c>BudgetService</c>: a merchant rule (the owner's own decision, D58)
/// always wins; then this map; then Uncategorized. A detailed code is looked up before its primary, so a
/// primary can carry a default while one of its details says otherwise. Names are matched to the ACTIVE
/// categories case-insensitively at use; a name the owner has renamed or removed simply finds no card and
/// falls through to Uncategorized.</para>
/// </summary>
public static class PlaidCategoryMap
{
    // The starter categories the app seeds (SettingsStore.BudgetCategories); a test pins that every
    // target below is one of them.
    private const string Groceries = "Groceries";
    private const string DiningOut = "Dining out";
    private const string Fuel = "Fuel";
    private const string Household = "Household";

    // null target = NotSpending.
    private readonly record struct Row(string PlaidCode, string? Target);

    private static readonly Row[] Rows =
    [
        // ---- not spending (D140) ----
        new("INCOME", null),                                    // pay, interest, dividends, tax refunds: money in
        new("TRANSFER_IN", null),                               // money in from another account or person
        new("TRANSFER_OUT", null),                              // savings and account moves, ATM cash, person-to-person
        new("LOAN_PAYMENTS", null),                             // card, car, mortgage, student-loan payments: Snowball's

        // ---- spending categories (detailed codes) ----
        new("FOOD_AND_DRINK_GROCERIES", Groceries),             // supermarkets
        new("FOOD_AND_DRINK_RESTAURANT", DiningOut),            // sit-down restaurants
        new("FOOD_AND_DRINK_FAST_FOOD", DiningOut),             // fast food
        new("FOOD_AND_DRINK_COFFEE", DiningOut),                // coffee shops
        new("TRANSPORTATION_GAS", Fuel),                        // petrol stations

        // ---- spending categories (whole primary) ----
        new("HOME_IMPROVEMENT", Household),                     // hardware, furniture, repairs, security
    ];

    private static readonly Dictionary<string, string?> Lookup =
        Rows.ToDictionary(row => row.PlaidCode, row => row.Target, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every category name this map can assign — what the test checks against the seeded list.</summary>
    public static IReadOnlyList<string> TargetCategoryNames { get; } =
        Rows.Where(row => row.Target is not null).Select(row => row.Target!).Distinct().ToList();

    public static PlaidCategoryDecision Decide(string? primary, string? detailed)
    {
        if (!string.IsNullOrEmpty(detailed) && Lookup.TryGetValue(detailed, out var byDetail))
        {
            return From(byDetail);
        }
        if (!string.IsNullOrEmpty(primary) && Lookup.TryGetValue(primary, out var byPrimary))
        {
            return From(byPrimary);
        }
        return new PlaidCategoryDecision(PlaidCategoryKind.Unmapped, null);
    }

    public static bool IsNotSpending(string? primary, string? detailed) =>
        Decide(primary, detailed).Kind == PlaidCategoryKind.NotSpending;

    private static PlaidCategoryDecision From(string? target) => target is null
        ? new PlaidCategoryDecision(PlaidCategoryKind.NotSpending, null)
        : new PlaidCategoryDecision(PlaidCategoryKind.Category, target);
}
