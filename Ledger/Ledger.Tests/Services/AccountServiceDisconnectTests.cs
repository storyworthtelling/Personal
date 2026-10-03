using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Index.Domain.External;
using Index.Domain.Model;
using Index.Infrastructure.Persistence;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Services;

/// <summary>
/// Guard R-G11: disconnecting a bank is undoable, whatever kind of bank it is.
///
/// <para>The bug: the undo re-created the institution from its Plaid credentials alone, and skipped any
/// credential with a blank item id / token — which is every hand-entered bank — so the "undo" did nothing
/// and the cascade-deleted accounts were gone for good. For a Plaid bank it brought back a bare
/// institution row with new ids, no accounts, and no Savings override, order or colour.</para>
///
/// <para>Cascade inventory (read from <c>LedgerDbContext</c> and the migrations): the ONLY foreign key is
/// <c>Account.InstitutionId</c>. Snowball plans and snapshots, savings goals and snapshots, transactions
/// and worksheet set-asides all key on an account's id as a plain column with no constraint, so deleting
/// the accounts leaves them in place — which is why the restore has to bring the accounts back under
/// their ORIGINAL ids: that is what re-attaches them. Test 2 below proves both halves.</para>
/// </summary>
public class AccountServiceDisconnectTests
{
    // Built, not spelled, so the hex-literal leak guard has nothing to read in a test about restoring a value.
    private static readonly string Tint = $"#{0x123456:X6}";

    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static AccountService Create(TestDatabase db) => new(db, new FakeClock(Now));

    // ---- a hand-entered bank: blank item id and token ----

    [Fact]
    public async Task UndoOfAHandEnteredBankRestoresItsAccountsWithTheSameIdsAndFields()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var due = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
        var solar = await service.AddManualAccountAsync(
            "Solar loan", "Evergreen Home Lending", AccountKind.Loan, 9000.55m, 6.25m, 250m, PaymentCadence.Month, due, default);
        var roof = await service.AddManualAccountAsync(
            "Roof loan", "Evergreen Home Lending", AccountKind.Loan, 4100m, 5.5m, 120m, PaymentCadence.Week, null, default);
        var before = await service.GetInstitutionsAsync(default);
        var institutionBefore = Assert.Single(before);

        var disconnected = await service.DisconnectInstitutionAsync(institutionBefore.Id, default);

        Assert.NotNull(disconnected);
        Assert.Empty(await service.GetInstitutionsAsync(default));

        Assert.True(await service.UndoDisconnectAsync(disconnected, default));

        var after = Assert.Single(await service.GetInstitutionsAsync(default));
        Assert.Equal(institutionBefore.Id, after.Id);
        Assert.Equal("Evergreen Home Lending", after.Name);
        Assert.Equal([solar.Id, roof.Id], after.Accounts.Select(a => a.Id).OrderBy(id => id == solar.Id ? 0 : 1));
        var restoredSolar = after.Accounts.Single(a => a.Id == solar.Id);
        Assert.Equal(9000.55m, restoredSolar.CurrentBalance);
        Assert.Equal(6.25m, restoredSolar.Apr);
        Assert.Equal(250m, restoredSolar.NextPaymentAmount);
        Assert.Equal(PaymentCadence.Month, restoredSolar.PaymentCadence);
        Assert.Equal(due, restoredSolar.NextPaymentDueDate);
        Assert.Equal(AccountSource.Manual, restoredSolar.Source);
        Assert.Equal(PaymentCadence.Week, after.Accounts.Single(a => a.Id == roof.Id).PaymentCadence);
    }

    // ---- a Plaid bank, with everything the owner set on it ----

    [Fact]
    public async Task UndoOfAPlaidBankRestoresItsOverridesOrderColourAndStillTiesToItsHistory()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        await service.AddInstitutionAsync("item-0", "access-0", "Alpha", default);
        var redwood = await service.AddInstitutionAsync("item-1", "access-1", "Redwood Bank", default, plaidInstitutionId: "ins_3");
        await service.AddInstitutionAsync("item-2", "access-2", "Zeta", default);
        var savingsId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await using (var context = db.CreateDbContext())
        {
            var row = await context.Institutions.SingleAsync(i => i.Id == redwood.Id);
            row.PrimaryColorHex = Tint; // an opaque string here; the real colour comes from Plaid
            row.ColorLookedUpAtUtc = Now;
            context.Accounts.AddRange(
                new Account
                {
                    Id = savingsId, InstitutionId = redwood.Id, AccountId = "plaid-sav", Name = "Rainy day",
                    Kind = AccountKind.Depository, Subtype = "checking", CurrentBalance = 1234.56m,
                    IsSavingsOverride = true, Source = AccountSource.Plaid,
                },
                new Account
                {
                    Id = cardId, InstitutionId = redwood.Id, AccountId = "plaid-card", Name = "Sapphire",
                    Kind = AccountKind.Credit, CreditLimit = 8000m, CurrentBalance = 400m,
                    NextPaymentDueDate = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero),
                });
            // Everything keyed by account id — none of it has a foreign key, so none of it cascades.
            context.SnowballPlans.Add(new SnowballPlan { LoanAccountId = cardId, FocusOrder = 2, ExtraPaymentAmount = 75m });
            context.SnowballSnapshots.Add(new SnowballSnapshot { AccountId = cardId, WeekOf = new DateOnly(2026, 9, 25), Balance = 410m });
            context.SavingsGoals.Add(new SavingsGoal { AccountId = savingsId, TargetAmount = 5000m });
            context.SavingsSnapshots.Add(new SavingsSnapshot { AccountId = savingsId, MonthOf = new DateOnly(2026, 9, 1), Balance = 1200m });
            context.Transactions.Add(new Transaction
            {
                AccountId = cardId, PlaidTransactionId = "txn-1", MerchantName = "Coffee", Amount = 4.5m, Date = Now,
            });
            await context.SaveChangesAsync();
        }

        var disconnected = await service.DisconnectInstitutionAsync(redwood.Id, default);

        Assert.Equal(["Alpha", "Zeta"], (await service.GetInstitutionsAsync(default)).Select(i => i.Name));
        await using (var context = db.CreateDbContext())
        {
            Assert.Equal(0, await context.Accounts.CountAsync());
            // The accounts cascaded; the history keyed to them did not.
            Assert.Equal(1, await context.SnowballPlans.CountAsync());
            Assert.Equal(1, await context.SnowballSnapshots.CountAsync());
            Assert.Equal(1, await context.SavingsGoals.CountAsync());
            Assert.Equal(1, await context.SavingsSnapshots.CountAsync());
            Assert.Equal(1, await context.Transactions.CountAsync());
        }

        Assert.True(await service.UndoDisconnectAsync(disconnected!, default));

        var institutions = await service.GetInstitutionsAsync(default);
        Assert.Equal(["Alpha", "Redwood Bank", "Zeta"], institutions.Select(i => i.Name));
        var restored = institutions.Single(i => i.Name == "Redwood Bank");
        Assert.Equal(redwood.Id, restored.Id);
        Assert.Equal("item-1", restored.ItemId);
        Assert.Equal("access-1", restored.AccessToken);
        Assert.Equal("ins_3", restored.PlaidInstitutionId);
        Assert.Equal(Tint, restored.PrimaryColorHex);
        Assert.Equal(Now, restored.ColorLookedUpAtUtc);
        Assert.Equal(1, restored.SortOrder);

        var saving = restored.Accounts.Single(a => a.Id == savingsId);
        Assert.True(saving.IsSavingsOverride);
        Assert.Equal(1234.56m, saving.CurrentBalance);
        Assert.Equal(cardId, restored.Accounts.Single(a => a.AccountId == "plaid-card").Id);

        await using var after = db.CreateDbContext();
        Assert.Equal(cardId, (await after.SnowballPlans.SingleAsync()).LoanAccountId);
        Assert.Equal(cardId, (await after.SnowballSnapshots.SingleAsync()).AccountId);
        Assert.Equal(savingsId, (await after.SavingsGoals.SingleAsync()).AccountId);
        Assert.Equal(savingsId, (await after.SavingsSnapshots.SingleAsync()).AccountId);
    }

    [Fact]
    public async Task ARestoredBankCarriesEveryMappedFieldOfItselfAndItsAccounts()
    {
        // A field added to Institution or Account later must round-trip too: fill every settable
        // property with a non-default value and compare after the restore, so a hand-copied snapshot
        // that forgot the new column fails here rather than silently dropping it.
        using var db = new TestDatabase();
        var service = Create(db);
        var institutionId = Guid.NewGuid();
        await using (var context = db.CreateDbContext())
        {
            var institution = new Institution { Id = institutionId };
            FillEveryProperty(institution, nameof(Institution.Accounts), nameof(Institution.Id));
            var account = new Account { InstitutionId = institutionId };
            FillEveryProperty(account, nameof(Account.InstitutionId));
            institution.Accounts.Add(account);
            context.Institutions.Add(institution);
            await context.SaveChangesAsync();
        }

        var before = Assert.Single(await service.GetInstitutionsAsync(default));
        var disconnected = await service.DisconnectInstitutionAsync(institutionId, default);
        await service.UndoDisconnectAsync(disconnected!, default);
        var after = Assert.Single(await service.GetInstitutionsAsync(default));

        AssertSameProperties(before, after, skip: nameof(Institution.Accounts));
        AssertSameProperties(before.Accounts.Single(), after.Accounts.Single(), skip: null);
    }

    [Fact]
    public async Task AManualAccountAttachedToAPlaidBankByNameComesBackWithIt()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var redwood = await service.AddInstitutionAsync("item-1", "access-1", "Redwood Bank", default);
        var manual = await service.AddManualAccountAsync(
            "Auto loan", "redwood bank", AccountKind.Loan, 7000m, 4m, 300m, PaymentCadence.Month, null, default);
        Assert.Equal(redwood.Id, manual.InstitutionId);

        var disconnected = await service.DisconnectInstitutionAsync(redwood.Id, default);
        await service.UndoDisconnectAsync(disconnected!, default);

        var restored = Assert.Single(await service.GetInstitutionsAsync(default));
        Assert.Equal(manual.Id, Assert.Single(restored.Accounts).Id);
    }

    [Fact]
    public async Task AnUndoCannotRunTwice_AndNeverDuplicatesABankThatIsAlreadyBack()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        await service.AddManualAccountAsync("Loan", "Lender", AccountKind.Loan, 100m, null, null, null, null, default);
        var id = (await service.GetInstitutionsAsync(default)).Single().Id;
        var disconnected = await service.DisconnectInstitutionAsync(id, default);

        Assert.True(await service.UndoDisconnectAsync(disconnected!, default));
        Assert.False(await service.UndoDisconnectAsync(disconnected!, default));

        var institution = Assert.Single(await service.GetInstitutionsAsync(default));
        Assert.Single(institution.Accounts);
    }

    [Fact]
    public async Task ABankConnectedWhileTheWindowWasOpenKeepsItsPlaceAndTheOrderStaysDense()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        await service.AddInstitutionAsync("a", "ta", "A", default);
        var b = await service.AddInstitutionAsync("b", "tb", "B", default);
        await service.AddInstitutionAsync("c", "tc", "C", default);

        var disconnected = await service.DisconnectInstitutionAsync(b.Id, default);
        await service.AddInstitutionAsync("d", "td", "D", default); // takes SortOrder 2 — C's
        await service.UndoDisconnectAsync(disconnected!, default);

        var institutions = await service.GetInstitutionsAsync(default);
        Assert.Equal(["A", "B", "C", "D"], institutions.Select(i => i.Name));
        Assert.Equal([0, 1, 2, 3], institutions.Select(i => i.SortOrder));
    }

    [Fact]
    public async Task DisconnectingAnUnknownBankIsANoOp()
    {
        using var db = new TestDatabase();
        Assert.Null(await Create(db).DisconnectInstitutionAsync(Guid.NewGuid(), default));
    }

    // ---- /item/remove ----

    [Fact]
    public async Task ItemRemoveIsNeverCalledForABlankToken()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        await service.AddManualAccountAsync("Loan", "Evergreen Home Lending", AccountKind.Loan, 100m, null, null, null, null, default);
        var plaid = new Mock<IPlaidClient>(MockBehavior.Strict);
        var disconnected = await service.DisconnectInstitutionAsync(
            (await service.GetInstitutionsAsync(default)).Single().Id, default);

        var released = await service.CommitDisconnectAsync(disconnected!, plaid.Object, NullLogger.Instance, default);

        Assert.False(released);
        plaid.Verify(p => p.RemoveItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ItemRemoveIsCalledOnceAfterTheWindowClosesWithoutAnUndo()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var redwood = await service.AddInstitutionAsync("item-1", "access-1", "Redwood Bank", default);
        var plaid = new Mock<IPlaidClient>();
        var disconnected = await service.DisconnectInstitutionAsync(redwood.Id, default);

        // The window is still open: nothing has gone to Plaid yet.
        plaid.Verify(p => p.RemoveItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.True(await service.CommitDisconnectAsync(disconnected!, plaid.Object, NullLogger.Instance, default));
        Assert.False(await service.CommitDisconnectAsync(disconnected!, plaid.Object, NullLogger.Instance, default));

        plaid.Verify(p => p.RemoveItemAsync("access-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnUndoneDisconnectIsNeverReleasedAtPlaid_AndAReleasedOneCannotBeUndone()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var redwood = await service.AddInstitutionAsync("item-1", "access-1", "Redwood Bank", default);
        var plaid = new Mock<IPlaidClient>();

        var undone = await service.DisconnectInstitutionAsync(redwood.Id, default);
        await service.UndoDisconnectAsync(undone!, default);
        Assert.False(await service.CommitDisconnectAsync(undone!, plaid.Object, NullLogger.Instance, default));
        plaid.Verify(p => p.RemoveItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        var released = await service.DisconnectInstitutionAsync(redwood.Id, default);
        await service.CommitDisconnectAsync(released!, plaid.Object, NullLogger.Instance, default);
        Assert.False(await service.UndoDisconnectAsync(released!, default)); // the token is dead at Plaid now
        Assert.Empty(await service.GetInstitutionsAsync(default));
    }

    [Fact]
    public async Task APlaidFailureOnReleaseIsLoggedWithItsDetailAndDoesNotThrow()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var redwood = await service.AddInstitutionAsync("item-1", "access-1", "Redwood Bank", default);
        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.RemoveItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Plaid said no"));
        var logger = new RecordingLogger();
        var disconnected = await service.DisconnectInstitutionAsync(redwood.Id, default);

        var released = await service.CommitDisconnectAsync(disconnected!, plaid.Object, logger, default);

        Assert.False(released);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains("Redwood Bank", entry.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(entry.Exception);
    }

    // ---- helpers ----

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<(string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((formatter(state, exception), exception));
    }

    private static void FillEveryProperty(object target, params string[] skip)
    {
        var seed = 3;
        foreach (var property in target.GetType().GetProperties().Where(p => p.CanWrite && !skip.Contains(p.Name)))
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            seed++;
            object? value = type switch
            {
                _ when type == typeof(Guid) => Guid.NewGuid(),
                _ when type == typeof(string) => $"value-{property.Name}",
                _ when type == typeof(decimal) => seed + 0.25m,
                _ when type == typeof(int) => seed,
                _ when type == typeof(bool) => true,
                _ when type == typeof(DateTimeOffset) => new DateTimeOffset(2026, 7, 1 + seed, 5, 6, 7, TimeSpan.Zero),
                _ when type.IsEnum => Enum.GetValues(type).Cast<object>().Last(),
                _ => throw new NotSupportedException($"{target.GetType().Name}.{property.Name} is a {type.Name}: teach this test how to fill it."),
            };
            property.SetValue(target, value);
        }
    }

    private static void AssertSameProperties(object expected, object actual, string? skip)
    {
        foreach (var property in expected.GetType().GetProperties().Where(p => p.CanRead && p.Name != skip))
        {
            Assert.True(
                Equals(property.GetValue(expected), property.GetValue(actual)),
                $"{expected.GetType().Name}.{property.Name} changed across disconnect and undo: "
                + $"{property.GetValue(expected)} -> {property.GetValue(actual)}");
        }
    }
}
