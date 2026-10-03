using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Index.Domain.Model;
using Index.Domain.Worksheet;

namespace Index.Infrastructure.Persistence;

// The data-access layer (per the repo's architecture: EF Core DbContext directly, no repository
// wrapper). All refresh state lives here so the background worker resumes purely from the database.
public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options)
    : DbContext(options)
{
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<SnowballPlan> SnowballPlans => Set<SnowballPlan>();
    public DbSet<SnowballSnapshot> SnowballSnapshots => Set<SnowballSnapshot>();
    public DbSet<BudgetCategory> BudgetCategories => Set<BudgetCategory>();
    public DbSet<BudgetMonth> BudgetMonths => Set<BudgetMonth>();
    public DbSet<BudgetCategoryAmount> BudgetCategoryAmounts => Set<BudgetCategoryAmount>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<MerchantCategoryRule> MerchantCategoryRules => Set<MerchantCategoryRule>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
    public DbSet<SavingsSnapshot> SavingsSnapshots => Set<SavingsSnapshot>();
    public DbSet<WorksheetSetAsideAmount> WorksheetSetAsideAmounts => Set<WorksheetSetAsideAmount>();

    // SQLite has no native DateTimeOffset, and the provider cannot translate comparisons on the
    // default TEXT mapping. Storing UTC ticks as integers makes the worker's "due institution" time
    // comparisons translate to plain integer comparisons.
    private static readonly ValueConverter<DateTimeOffset, long> TicksConverter =
        new(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableTicksConverter =
        new(value => value.HasValue ? value.Value.UtcTicks : null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);

    // Same reasoning as the DateTimeOffset converters above: a fixed, explicit "yyyy-MM-dd" TEXT shape
    // is worth trusting over whatever the SQLite provider's own DateOnly translation happens to do,
    // for a value SnowballSnapshot's dedupe-at-write logic compares directly (Guard G8).
    private static readonly ValueConverter<DateOnly, string> DateOnlyConverter =
        new(value => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            value => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture));

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Institution>(entity =>
        {
            entity.HasKey(institution => institution.Id);
            entity.Property(institution => institution.RefreshStatus).HasConversion<int>();
            entity.Property(institution => institution.NextAttemptAtUtc).HasConversion(NullableTicksConverter);
            entity.Property(institution => institution.LastRefreshedUtc).HasConversion(NullableTicksConverter);
            entity.Property(institution => institution.CreatedAtUtc).HasConversion(TicksConverter);
            entity.Property(institution => institution.ColorLookedUpAtUtc).HasConversion(NullableTicksConverter);
            entity.HasMany(institution => institution.Accounts)
                .WithOne()
                .HasForeignKey(account => account.InstitutionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(institution => new { institution.RefreshStatus, institution.NextAttemptAtUtc });
        });

        model.Entity<Account>(entity =>
        {
            entity.HasKey(account => account.Id);
            entity.Property(account => account.Kind).HasConversion<int>();
            entity.Property(account => account.BalanceAsOfUtc).HasConversion(NullableTicksConverter);
            entity.Property(account => account.NextPaymentDueDate).HasConversion(NullableTicksConverter);
            entity.Property(account => account.Source).HasConversion<int>();
            entity.Property(account => account.PaymentCadence).HasConversion<int?>();
            entity.Property(account => account.BalanceSource).HasConversion<int?>();
            entity.HasIndex(account => new { account.InstitutionId, account.AccountId });
        });

        model.Entity<SnowballPlan>(entity =>
        {
            entity.HasKey(plan => plan.Id);
            // One plan row per loan account (D04/D43) — SnowballService.EnsurePlansExistAsync is the
            // only writer of new rows, and this is its own backstop against ever creating two.
            entity.HasIndex(plan => plan.LoanAccountId).IsUnique();
        });

        model.Entity<SnowballSnapshot>(entity =>
        {
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.WeekOf).HasConversion(DateOnlyConverter);
            // D78's dedupe-at-write, backstopped at the database: SnowballService checks first, this
            // index is what stops a second writer (or a bug) from ever landing a duplicate row.
            entity.HasIndex(snapshot => new { snapshot.AccountId, snapshot.WeekOf }).IsUnique();
        });

        // ---- Release E: Budget (finding §3, D55/D66/D67) ----

        model.Entity<BudgetCategory>(entity =>
        {
            entity.HasKey(category => category.Id);
        });

        model.Entity<BudgetMonth>(entity =>
        {
            entity.HasKey(month => month.Id);
            entity.Property(month => month.Status).HasConversion<int>();
            // One row per calendar month — BudgetService.GetOrCreateMonthAsync checks first, this
            // index is the backstop, the same shape SnowballSnapshot's dedupe-at-write index above uses.
            entity.HasIndex(month => month.YearMonth).IsUnique();
        });

        model.Entity<BudgetCategoryAmount>(entity =>
        {
            entity.HasKey(amount => amount.Id);
            // One row per (month, category) — EnsureCategoryAmountsAsync's own backstop.
            entity.HasIndex(amount => new { amount.BudgetMonthId, amount.CategoryId }).IsUnique();
        });

        model.Entity<Transaction>(entity =>
        {
            entity.HasKey(transaction => transaction.Id);
            // Same reasoning as Institution/Account's own DateTimeOffset columns above: SQLite has no
            // native DateTimeOffset, and a range comparison (D66's purge, every month-range read) does
            // not translate on the default TEXT mapping without this.
            entity.Property(transaction => transaction.Date).HasConversion(TicksConverter);
            // D66's retention purge selects by date range, and RefreshTransactionsAsync's upsert
            // matches an existing row by Plaid's own transaction id — both read this index.
            entity.HasIndex(transaction => transaction.Date);
            entity.HasIndex(transaction => transaction.PlaidTransactionId).IsUnique();
        });

        model.Entity<MerchantCategoryRule>(entity =>
        {
            entity.HasKey(rule => rule.Id);
            // Not a unique DB constraint on MerchantName: matching is done case-insensitively in code
            // (BudgetService, the same in-memory-match convention BalanceReceiverMatch already uses),
            // and SQLite's default TEXT collation is case-sensitive — a DB-level unique index here
            // would let "Costco"/"COSTCO" collide past it while the app's own lookup treats them as
            // one merchant, which is a worse failure mode than the duplicate this index would catch.
            entity.HasIndex(rule => rule.MerchantName);
        });

        // ---- Release G: Savings (D50/D81/D82) ----

        model.Entity<SavingsGoal>(entity =>
        {
            entity.HasKey(goal => goal.Id);
            // D50: one goal per account, not one household total — SavingsService.SetGoalAsync's own
            // upsert-by-AccountId backstop.
            entity.HasIndex(goal => goal.AccountId).IsUnique();
        });

        model.Entity<SavingsSnapshot>(entity =>
        {
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.MonthOf).HasConversion(DateOnlyConverter);
            // D81's dedupe-at-write, the same shape SnowballSnapshot's own index uses above, but keyed
            // to a calendar MONTH rather than a week.
            entity.HasIndex(snapshot => new { snapshot.AccountId, snapshot.MonthOf }).IsUnique();
        });

        // ---- Release H: zero-based (D98/D99/D100) ----

        model.Entity<WorksheetSetAsideAmount>(entity =>
        {
            entity.HasKey(amount => amount.Id);
            // One row per (month, savings account) — BudgetService's own upsert backstop, the same
            // shape BudgetCategoryAmount's own index uses above.
            entity.HasIndex(amount => new { amount.BudgetMonthId, amount.SavingsAccountId }).IsUnique();
        });
    }
}
