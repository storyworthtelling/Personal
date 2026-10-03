using System.Text.Json;
using Index.Domain.Household;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Infrastructure.Services;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Index.Infrastructure.Household;

// Best-effort, matching the vault's own idiom (VaultSync.PublishAsync, SecretsVaultService.PublishAsync):
// never throws, and a skip/failure never blocks the refresh cycle that triggered it. The owner's Settings
// screen can surface Message when it wants to, the same way SetupPage already surfaces the vault's own
// publish result — but nothing today requires it to.
public sealed record HouseholdSnapshotPublishResult(bool Ok, string? Message)
{
    public static HouseholdSnapshotPublishResult Skipped(string reason) => new(true, reason);

    // Not a failure and not something to retry: the file belongs to another account and this install
    // is working as designed by leaving it alone. Ok so a refresh cycle never reads it as an error.
    public static HouseholdSnapshotPublishResult Refused(string reason) => new(true, reason);
    public static HouseholdSnapshotPublishResult Published() => new(true, null);
    public static HouseholdSnapshotPublishResult Failed(string message) => new(false, message);
}

/// <summary>
/// The owner's install's side of the household file (D10, finding §3's "Resolved" thread): builds
/// <see cref="HouseholdSnapshot"/> from the four tab services — nothing here computes a figure the
/// services don't already compute; this only serializes what <see cref="AccountService"/>,
/// <see cref="SnowballService"/>, <see cref="BudgetService"/> and <see cref="SavingsService"/> already
/// expose — encrypts it with the shared vault passphrase through the same
/// <see cref="SecretsCrypto"/>/<see cref="VaultEnvelope"/> path the vault itself uses, and writes it over
/// the shared <see cref="IDriveTextFile"/> seam <c>DriveAudioLibraryStore</c> established.
/// </summary>
public sealed class HouseholdSnapshotPublisher
{
    public const string FileName = HouseholdSnapshotReader.FileName;

    private readonly AccountService accounts;
    private readonly SnowballService snowball;
    private readonly BudgetService budget;
    private readonly SavingsService savings;
    private readonly IDriveTextFile file;
    private readonly IClock clock;

    public HouseholdSnapshotPublisher(
        AccountService accounts, SnowballService snowball, BudgetService budget, SavingsService savings,
        IDriveTextFile file, IClock clock)
    {
        this.accounts = accounts;
        this.snowball = snowball;
        this.budget = budget;
        this.savings = savings;
        this.file = file;
        this.clock = clock;
    }

    public async Task<HouseholdSnapshotPublishResult> PublishAsync(
        string passphrase, string publishedByAccountEmail, CancellationToken cancellationToken)
    {
        // D10/D26's own precondition: nothing to encrypt with, or nothing to say published this. Both
        // are ordinary, quiet states on a fresh install (mirrors VaultSync/SecretsVaultService's own
        // "no passphrase yet" skip) — never an error to surface.
        if (string.IsNullOrEmpty(passphrase))
        {
            return HouseholdSnapshotPublishResult.Skipped("No vault passphrase set yet.");
        }
        if (string.IsNullOrEmpty(publishedByAccountEmail))
        {
            return HouseholdSnapshotPublishResult.Skipped("No Google account signed in yet.");
        }

        try
        {
            // Never take over a file another account published. Her install publishing here would
            // stamp HER address on the owner's snapshot, after which the owner's own install resolves
            // as Hers and goes read-only. The existing file is read first, with the same passphrase,
            // and a read that cannot say who published it is also a refusal — overwriting what could not
            // be opened is how someone else's data is lost. A damaged file (not an envelope at all) has
            // no publisher to protect and may be replaced.
            var existing = await new HouseholdSnapshotReader(file).ReadAsync(passphrase, cancellationToken);
            if (existing.Status == HouseholdSnapshotStatus.WrongPassphrase)
            {
                return HouseholdSnapshotPublishResult.Refused(
                    "The household file is locked with a different passphrase; not replacing it.");
            }
            // Never downgrade what it cannot understand: a file a newer Index wrote would be replaced by this
            // build's older shape, losing whatever the newer one added, with nothing on either phone saying
            // so. This holds even for the account that published it — the rule is about the shape, not the
            // owner. (The stamp below says which version THIS build writes.)
            if (existing.Status == HouseholdSnapshotStatus.NewerVersion)
            {
                return HouseholdSnapshotPublishResult.Refused(
                    "The household file was written by a newer version of Index; not replacing it.");
            }
            if (existing.Status == HouseholdSnapshotStatus.Loaded
                && !HouseholdFileGate.MayPublishOver(publishedByAccountEmail, existing.Snapshot!.PublishedByAccountEmail))
            {
                return HouseholdSnapshotPublishResult.Refused(
                    "The household file was published by another account; not replacing it.");
            }

            var snapshot = await BuildSnapshotAsync(publishedByAccountEmail, cancellationToken);
            var json = JsonSerializer.Serialize(snapshot, VaultEnvelope.SerializerOptions);
            await file.WriteAsync(VaultEnvelope.Write(json, passphrase), cancellationToken);
            return HouseholdSnapshotPublishResult.Published();
        }
        catch (Exception ex)
        {
            return HouseholdSnapshotPublishResult.Failed(ex.Message);
        }
    }

    private async Task<HouseholdSnapshot> BuildSnapshotAsync(string publishedByAccountEmail, CancellationToken cancellationToken)
    {
        var institutions = await accounts.GetInstitutionsAsync(cancellationToken);
        var householdInstitutions = institutions
            .Select(institution =>
            {
                var open = institution.Accounts.Where(a => !AccountStatus.IsPaidOff(a)).ToList();
                return (Institution: institution, OpenAccounts: open);
            })
            // D124's own rule, reused here: a bank with nothing open left has nothing for her Accounts
            // tab to show either.
            .Where(x => x.OpenAccounts.Count > 0)
            .Select(x => new HouseholdInstitution(
                x.Institution.Name,
                IsStale: x.Institution.RefreshStatus == WorkStatus.Failed,
                AsOfUtc: x.Institution.LastRefreshedUtc,
                x.OpenAccounts.Select(a => new HouseholdAccount(
                    a.Name, a.Mask, a.Kind, a.CurrentBalance, a.CreditLimit,
                    a.NextPaymentDueDate, a.NextPaymentAmount, a.IsoCurrencyCode, a.Subtype)).ToList(),
                IsHandEntered: string.IsNullOrEmpty(x.Institution.AccessToken)))
            .ToList();

        var snowballSummary = await snowball.GetSummaryAsync(cancellationToken);
        var snowballDto = new HouseholdSnowballSummary(
            snowballSummary.DebtFreeDate,
            snowballSummary.TotalWeeks,
            snowballSummary.TotalOwed,
            snowballSummary.ExtraPaymentAmount,
            snowballSummary.LoansInDisplayOrder.Select(loan => new HouseholdSnowballLoan(
                loan.Name, loan.DisplayNumber, loan.IsFocus, loan.IsPaidOff,
                loan.Balance, loan.Apr, loan.WeeklyPaymentNow, loan.PayoffDate,
                loan.FocusedWeeklyPayment, loan.WeeksBeforeFocused, loan.AmountBeforeFocused, loan.WeeksToPayoff)).ToList());

        var budgetDto = await BuildBudgetSummaryAsync(cancellationToken);

        var savingsSummary = await savings.GetAccountsAsync(cancellationToken);
        var savingsDto = new HouseholdSavingsSummary(
            savingsSummary.TotalSaved,
            savingsSummary.Accounts.Select(a => new HouseholdSavingsAccount(
                a.InstitutionName, a.AccountName, a.Balance, a.GoalTargetAmount, a.MonthlyPace, a.MonthsOfHistory)).ToList());

        // Version 2 (remediation 3.6): the two series her History and Growth sides draw from, computed from
        // the owner's database now, by the same service calls the owner's own pages make.
        var snowballHistory = HouseholdChartSeries.FromSnowballHistory(await snowball.GetHistoryAsync(cancellationToken));
        var savingsGrowth = HouseholdChartSeries.FromSavingsGrowth(await savings.GetGrowthAsync(cancellationToken));

        return new HouseholdSnapshot(
            clock.UtcNow, publishedByAccountEmail, householdInstitutions, snowballDto, budgetDto, savingsDto,
            HouseholdSnapshot.CurrentVersion, snowballHistory, savingsGrowth);
    }

    // D88's own gate, reproduced rather than reinvented: before any worksheet exists for the current
    // month there is nothing to show — her Budget tab reads the same empty summary and renders the same
    // quiet empty state BudgetPage.BuildEmptyState already does on the owner's own install.
    private async Task<HouseholdBudgetSummary> BuildBudgetSummaryAsync(CancellationToken cancellationToken)
    {
        var yearMonth = BudgetPeriod.YearMonthOf(DateOnly.FromDateTime(clock.UtcNow.LocalDateTime.Date));
        var month = await budget.FindMonthAsync(yearMonth, cancellationToken);
        if (month is null)
        {
            return new HouseholdBudgetSummary(yearMonth, TrackingOnly: false, Categories: []);
        }

        // The same call the owner's Budget page draws from, so the two phones read one set of figures:
        // the visible categories' amounts, the spending-only Uncategorized total (D140) and last month's
        // recorded spend per category (D142). A second hand-rolled query here is how her month total once
        // came to leave out the uncategorised spending the owner's includes.
        var figures = await budget.GetMonthFiguresAsync(month, cancellationToken);
        var amountsByCategory = figures.Amounts.ToDictionary(a => a.CategoryId);
        // TrackingOnly is a whole-month switch (D95/D96's own remark on BudgetCategoryAmount.TrackingOnly)
        // stamped onto every category's row the same way when it's ensured — any one row's value is the
        // month's value.
        var trackingOnly = figures.Amounts.Any(a => a.TrackingOnly);

        var rows = figures.Categories.Select(category =>
        {
            amountsByCategory.TryGetValue(category.Id, out var amount);
            decimal? lastMonth = figures.LastMonthByCategory is { } previous && previous.TryGetValue(category.Id, out var spent)
                ? spent
                : null;
            return new HouseholdBudgetCategory(
                category.Name,
                amount?.LimitAmount ?? 0m,
                amount?.ActualAmount ?? 0m,
                amount?.TrackingOnly ?? trackingOnly,
                amount?.LimitUnreadable ?? false,
                lastMonth);
        }).ToList();

        return new HouseholdBudgetSummary(yearMonth, trackingOnly, rows, figures.UncategorizedTotal, figures.UncategorizedCount);
    }
}
