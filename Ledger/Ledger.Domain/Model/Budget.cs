namespace Index.Domain.Model;

// D66's four stages a month moves through, once the worksheet/reMarkable round-trip exists
// (Release F/H build the middle two; this release only needs Draft and Approved to exist and to
// mean something for the retention rule below).
public enum BudgetMonthStatus
{
    Draft,
    DeliveredToRemarkable,
    AwaitingConfirmation,
    Approved
}

// D55: a spending category. Soft-deleted, never dropped — removing one on Settings sets IsActive
// false so a past month's totals (BudgetCategoryAmount rows already keyed to this Id) keep reading
// correctly; only the active, visible list on Settings/Budget hides it. Release E migrates this off
// SettingsStore.BudgetCategories' JSON-encoded string list (see BudgetService.MigrateScaffoldIfNeededAsync).
public sealed class BudgetCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // The owner's own order on Settings/Budget (D123's pattern, reused via Index.Domain.Model.
    // InstitutionOrder's generic list arithmetic) — not alphabetical, not recomputed.
    public int SortOrder { get; set; }
}

// One calendar month's budget (finding §3). YearMonth is "yyyy-MM" — a plain sortable string rather
// than a DateOnly, since a month has no day to anchor to and BudgetPeriod's range/previous-month
// arithmetic is entirely string-in, string-out.
public sealed class BudgetMonth
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string YearMonth { get; set; } = string.Empty;
    public BudgetMonthStatus Status { get; set; } = BudgetMonthStatus.Draft;

    // Release F: D155's "a figure written there wins for the month" — the owner's hand-written
    // correction to the projected income, read back off the worksheet. Null until a correction is
    // read; the detected IncomeEstimate is what's used until then.
    public decimal? IncomeOverride { get; set; }

    // Release F, Guard G18: the active BudgetCategory ids, in the EXACT order they were printed on
    // this month's worksheet, as a JSON array of GUID strings. Recorded at delivery time so a later
    // read-back can reconstruct precisely which WorksheetRegions box belonged to which category even
    // if the live category list (additions, removals, reordering) has since moved on — the alternative,
    // re-deriving "today's active categories, in today's order" at read-back time, would silently
    // misattribute a box the moment the two orders disagree.
    public string? WorksheetCategoryOrderJson { get; set; }

    // Release H: the same idea, for the "Set aside" section's own box keys (WorksheetRegions.
    // SetAsideExtraPayment / SetAsideSavings), in the exact order printed — the focus loan or a savings
    // goal can change between print and read-back exactly the way the category list can.
    public string? WorksheetSetAsideOrderJson { get; set; }

    // Release H, D100/D155: the target this month's worksheet was printed against — income less the
    // month's Snowball payments, whole dollars — persisted at delivery time rather than recomputed at
    // read-back time. Recomputing live would compare what the owner wrote against TODAY's income
    // projection, which can have drifted from the figure actually printed on the sheet they are holding;
    // this is the single source of truth for what "balanced" means for this month. Null when income
    // wasn't confident at print time (D103) — nothing was printed to balance against, so no check runs.
    public long? PrintedZeroBasedTargetWholeDollars { get; set; }

    // Release H, D156's "never below last month's figure": the extra debt payment's OWN "last month"
    // reference, captured once at the ORIGINAL worksheet's print time. Unlike a category (whose "last
    // month" is the previous calendar month's own, unrelated BudgetMonth row — stable by construction)
    // or a savings goal (whose "last month" is Release G's own rolling pace, unaffected by this month's
    // limit writes), the extra payment has only ONE live figure in SnowballPlan.ExtraPaymentAmount — the
    // same figure a follow-up's own correction writes straight back into. Re-reading it live on a second
    // or third follow-up would compare the owner's new figure against ITS OWN just-applied correction
    // rather than what was actually printed, so it is frozen here instead.
    public long? PrintedExtraPaymentLastMonthWholeDollars { get; set; }
}

// One category's figures for one month (finding §3). LimitAmount/ActualAmount are both decimal —
// this is the first entity in the app whose money field is decimal from the day it is introduced,
// not migrated to it (MoneyIsDecimalTests, Guard G3).
public sealed class BudgetCategoryAmount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BudgetMonthId { get; set; }
    public Guid CategoryId { get; set; }

    public decimal LimitAmount { get; set; }
    public decimal ActualAmount { get; set; }

    // D95/D96: today this is read from the whole-month SettingsStore.BudgetTrackingOnly switch, not
    // decided per category — kept as its own column anyway because the finding's §3 schema names it,
    // and because it is what a genuinely per-category tracking mode (a later decision, not this one)
    // would flip without a migration. See BudgetPage's own remarks for which of the two this release
    // actually reads.
    public bool TrackingOnly { get; set; }

    // D49: the handwriting reader couldn't make this category's limit out on the worksheet, so it
    // was sent back to the reMarkable rather than guessed or asked on the phone. Not named in the
    // finding's §3 column list — added because mockup 8's third state needs somewhere durable to
    // hold it. Set by WorksheetReadBackService (and re-set by FollowUpReadBackService when a re-written
    // box is still unreadable); cleared by SetCategoryLimitAsync when a figure finally reads. The "sent
    // back" is real (R-G25): the next follow-up delivery carries the category as a row with a box of its
    // own, even for a month that balanced (FollowUpPlanBuilder / WorksheetDeliveryService).
    public bool LimitUnreadable { get; set; }
}

// One posted or pending transaction pulled from Plaid across every connected account (finding §3,
// D63). No BudgetMonthId — which month a transaction belongs to is always derived from its own Date
// (BudgetPeriod.Range), which is what lets D66's retention purge by date range alone rather than by a
// foreign key that would otherwise need its own cascade rule.
public sealed class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string PlaidTransactionId { get; set; } = string.Empty;
    public string MerchantName { get; set; } = string.Empty;

    // Plaid's own sign convention (positive = money leaving the account): summing this directly is
    // what "spent" and "left" both read off, and a refund (negative) nets out on its own.
    public decimal Amount { get; set; }
    public DateTimeOffset Date { get; set; }

    // Null = uncategorized (no MerchantCategoryRule matched it yet). Never defaults to a category —
    // an uncategorized transaction stays uncategorized until a rule exists or the owner moves it by
    // hand (D58), rather than landing somewhere silently wrong.
    public Guid? CategoryId { get; set; }

    // D58: true for the one transaction a recategorize action was taken on directly — the rule it
    // also wrote applies to every transaction AFTER this one, not this one (which was already placed
    // by the old rule, or by nothing, before the owner corrected it).
    public bool IsManualOverride { get; set; }

    // True while Plaid still reports the transaction as pending. A pending row is replaced in place
    // (same row, same owner-chosen category) when its posted successor arrives — R-G18.
    public bool Pending { get; set; }

    // Plaid's personal_finance_category, stored as sent (R8). Null on rows stored before the column
    // existed, or when Plaid sent none. PlaidCategoryMap reads these to decide a first category and to
    // keep income, transfers and loan/card payments out of "spent" (D140).
    public string? PlaidCategoryPrimary { get; set; }
    public string? PlaidCategoryDetailed { get; set; }
}

// D58/D67: a standing "this merchant means this category" rule, kept in its own table specifically so
// it outlives any individual Transaction row — D66's purge deletes old Transaction rows wholesale, and
// must never touch this table when it does (Guard: RuleSurvivesTransactionPurgeTests).
public sealed class MerchantCategoryRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MerchantName { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
}
