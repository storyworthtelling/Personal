using Index.Domain.Model;

namespace Index.Domain.Household;

/// <summary>
/// The finding's "Resolved — the four items Gate 1 left as 'Technical' open threads" (Computed-balances
/// Drive file): what the owner's install publishes and hers reads — <c>storyworthtelling-index-household
/// .json</c>, encrypted with the shared vault passphrase through <c>SecretsCrypto</c>/<c>SecretsEnvelope</c>
/// over the same <c>IDriveTextFile</c> seam <c>DriveAudioLibraryStore</c> established. Not a
/// <c>SecretsVault</c> section (CLAUDE.md's own "what this phase must not break": it "carries no risk of
/// colliding with another app's wire name") — its own file, its own shape.
///
/// <para>Computed balances (D10) plus the three tabs' summaries — enough for a read-only render of all
/// five of her tabs (D09/D29/D90) without a second live computation on her phone, which never touches
/// Plaid, EF or the SQLite tables the owner's services read from. Deliberately NOT the full EF shape:
/// no access tokens (D09 — nothing Plaid-shaped reaches her install at all), no per-transaction detail
/// (a summary, not a register) and no Ids that would only ever address rows on the OTHER phone's own
/// database.</para>
/// </summary>
public sealed record HouseholdSnapshot(
    DateTimeOffset PublishedAtUtc,

    // D44's mechanism: the Google account that published this file. InstallRoleResolver compares an
    // install's own signed-in account against this to tell the two installs apart, silently.
    string PublishedByAccountEmail,

    IReadOnlyList<HouseholdInstitution> Institutions,
    HouseholdSnowballSummary Snowball,
    HouseholdBudgetSummary Budget,
    HouseholdSavingsSummary Savings,

    // R7 (remediation 3.6) — the file's version. ABSENT means 1: a v1 file (everything before this field
    // existed) carries no "Version" key at all, and System.Text.Json fills a missing constructor
    // parameter with its default, so an old file reads as version 1 without a migration step. The
    // publisher writes CurrentVersion. A version above CurrentVersion is never deserialised into this
    // type at all (HouseholdSnapshotReader peeks at the number first) — a newer shape must be refused,
    // not misread. Any future version must keep "Version" and "PublishedByAccountEmail" at the top level,
    // because those two are all an older install needs to refuse the file and still know whose it is.
    int Version = HouseholdSnapshot.FirstVersion,

    // Version 2: the two series her History and Growth sides draw from. Null on a v1 file — which is the
    // only case her pages say "isn't part of this summary yet" (HouseholdChartCopy).
    HouseholdSnowballHistory? SnowballHistory = null,
    IReadOnlyList<HouseholdSavingsGrowth>? SavingsGrowth = null)
{
    /// <summary>What a file with no "Version" key is.</summary>
    public const int FirstVersion = 1;

    /// <summary>The newest shape this build can write and read.</summary>
    public const int CurrentVersion = 2;

    /// <summary>Whether the two series are internally consistent: every loan's balances run exactly the
    /// weeks' length, every savings account's balances exactly its months'. The series are written as
    /// aligned arrays (weeks once, a balance per week per loan) to keep the file small, which makes a
    /// length mismatch the one way a file can read as valid and draw wrong — so it is refused as damaged.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SeriesAreConsistent =>
        (SnowballHistory is null || (SnowballHistory.Weeks is not null && SnowballHistory.LoansInFocusOrder is not null
            && SnowballHistory.LoansInFocusOrder.All(l => l.Balances is not null && l.Balances.Count == SnowballHistory.Weeks.Count)))
        && (SavingsGrowth is null || SavingsGrowth.All(a =>
            a.Months is not null && a.Balances is not null && a.Balances.Count == a.Months.Count));
}

/// <summary>One bank heading (mockup 11's "Redwood Bank" / "Meridian Card" / "Harbor Auto Finance"), in the owner's own
/// order (D123) — the publisher writes <see cref="HouseholdSnapshot.Institutions"/> in that order and
/// nothing on the reading side re-sorts it.</summary>
public sealed record HouseholdInstitution(
    string Name,

    // D32: a broken Plaid connection still shows its last figures, marked stale ("As of Sep 23",
    // mockup 11's own Meridian Card row) — never silently outdated or hidden.
    bool IsStale,
    DateTimeOffset? AsOfUtc,

    IReadOnlyList<HouseholdAccount> Accounts,

    // Remediation 3.3 (mockups 2/15, D08): a hand-entered bank says "By hand" on its heading. ADDITIVE and
    // optional — no version bump (3.6's rule): a file written before it existed reads as false, and an older
    // build reading a newer file ignores it.
    bool IsHandEntered = false);

/// <summary>One account card's worth of facts (mockup 11's index-card rows) — no Mask/AccountId beyond
/// what the owner's own Accounts screen already shows unprotected, matching the diagnostics export's own
/// redaction rule (D145/Guard G14) applied here to a second Drive file.</summary>
public sealed record HouseholdAccount(
    string Name,
    string? Mask,
    AccountKind Kind,
    decimal? CurrentBalance,
    decimal? CreditLimit,
    DateTimeOffset? NextPaymentDueDate,
    decimal? NextPaymentAmount,
    string? IsoCurrencyCode,

    // Remediation 3.3 (mockup 2): Plaid's subtype ("checking", "savings"…) so her rows read "Checking ····7301",
    // not "Account ····7301". ADDITIVE and optional, same rule as <see cref="HouseholdInstitution.IsHandEntered"/>:
    // null on an older file, which falls back to "Account". Plaid's own category word, not an identifier.
    string? Subtype = null);

/// <summary>Snowball's Loans side (mockup 6), read-only on her phone (D90: no extra-payment field, no
/// reordering — the charts, the cards and their post-its are all there).</summary>
public sealed record HouseholdSnowballSummary(
    DateOnly DebtFreeDate,
    int TotalWeeks,
    decimal TotalOwed,
    decimal ExtraPaymentAmount,
    IReadOnlyList<HouseholdSnowballLoan> LoansInDisplayOrder);

public sealed record HouseholdSnowballLoan(
    string Name,
    int? DisplayNumber,
    bool IsFocus,
    bool IsPaidOff,
    decimal Balance,
    decimal Apr,
    decimal WeeklyPaymentNow,
    DateOnly PayoffDate,

    // The post-it's own once-it's-the-focus figures (mockup 6) — D90 keeps the post-it on her install
    // (only the extra-payment field and reordering go), so the snapshot carries what it needs.
    decimal FocusedWeeklyPayment,
    int WeeksBeforeFocused,
    decimal AmountBeforeFocused,
    int WeeksToPayoff);

/// <summary>The current month's Budget (mockup 7/8): categories with their limit/actual, enough for the
/// same index cards the owner's install draws. No transaction-level detail travels — a summary, not a
/// register (see this file's own remarks on why).</summary>
public sealed record HouseholdBudgetSummary(
    string YearMonth,
    bool TrackingOnly,
    IReadOnlyList<HouseholdBudgetCategory> Categories,
    decimal UncategorizedTotal = 0m,
    int UncategorizedCount = 0);

public sealed record HouseholdBudgetCategory(
    string Name,
    decimal LimitAmount,
    decimal ActualAmount,
    bool TrackingOnly,
    bool LimitUnreadable,
    decimal? LastMonthAmount = null);

/// <summary>Savings' Accounts side (mockup 9): every effectively-savings account's balance, goal and
/// pace — <c>SavingsPace.ReachesIt</c> is pure and cheap, so the reading side recomputes "Reaches it"
/// from these three figures rather than carrying a fourth, precomputed one that could drift from them.</summary>
public sealed record HouseholdSavingsSummary(
    decimal TotalSaved,
    IReadOnlyList<HouseholdSavingsAccount> Accounts);

public sealed record HouseholdSavingsAccount(
    string InstitutionName,
    string AccountName,
    decimal Balance,
    decimal? GoalTargetAmount,
    decimal MonthlyPace,
    int MonthsOfHistory);

/// <summary>
/// Version 2: Snowball's History side (mockup 6), the same shape the owner's own charts consume
/// (<c>SnowballHistory</c>) so her charts are drawn by the very same views. Weeks are written once and
/// each loan carries one balance per week, aligned to them (a (week, balance) pair per loan per week
/// would repeat every date ten times). A loan's peak is not stored: it is the maximum of its balances,
/// computed on the reading side exactly as the owner's service computes it.
/// </summary>
public sealed record HouseholdSnowballHistory(
    IReadOnlyList<DateOnly> Weeks,
    IReadOnlyList<HouseholdSnowballLoanHistory> LoansInFocusOrder)
{
    /// <summary>A safety bound, not a design limit: five years of weekly snapshots. The owner's History
    /// draws every recorded week and the sheet has 70, so nothing today is cut; a file that outlived five
    /// more years would keep the most recent window rather than grow without bound.</summary>
    public const int MaxWeeks = 260;
}

/// <summary>One loan's balances, in the same focus order as the Loans side (the legend's colour ramp is
/// that order). No account id, name-of-bank, mask or token: nothing that addresses a row on the other
/// phone or an account at the bank.</summary>
public sealed record HouseholdSnowballLoanHistory(
    string Name,
    bool IsMortgage,
    bool IsPaidOff,
    IReadOnlyList<decimal> Balances);

/// <summary>
/// Version 2: one savings account's Growth chart (mockup 9, second frame) — its recorded month-end
/// balances and its goal, if any. The dotted projection is not stored: the reading side works it out
/// from the account's pace on the Accounts summary (<c>SavingsPace.ReachesIt</c>), as it already does for
/// the card.
/// </summary>
public sealed record HouseholdSavingsGrowth(
    string InstitutionName,
    string AccountName,
    decimal? GoalTargetAmount,
    IReadOnlyList<DateOnly> Months,
    IReadOnlyList<decimal> Balances)
{
    /// <summary>The Growth chart's window: two years of months. The pace behind the projection looks at
    /// the last twelve, so nothing the chart or the card says depends on what is cut.</summary>
    public const int MaxMonths = 24;
}
