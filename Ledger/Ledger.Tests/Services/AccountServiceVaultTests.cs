using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Services;

// Backing up the per-Item Plaid access tokens to the shared secrets vault and restoring them after a
// reinstall (the FocusPhone cutover wipes the on-device database).
public class AccountServiceVaultTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static AccountService Create(TestDatabase db) => new(db, new FakeClock(Now));

    [Fact]
    public async Task GetInstitutionCredentials_ReturnsEachItemsTokens()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        await service.AddInstitutionAsync("item-1", "access-1", "Redwood Bank", default);
        await service.AddInstitutionAsync("item-2", "access-2", "PNC", default);

        var credentials = await service.GetInstitutionCredentialsAsync(default);

        Assert.Collection(credentials,
            c => Assert.Equal(("item-1", "access-1", "Redwood Bank"), (c.ItemId, c.AccessToken, c.Name)),
            c => Assert.Equal(("item-2", "access-2", "PNC"), (c.ItemId, c.AccessToken, c.Name)));
    }

    [Fact]
    public async Task RestoreInstitutions_AddsMissingItems_DueNow()
    {
        using var db = new TestDatabase();
        var service = Create(db);

        var added = await service.RestoreInstitutionsAsync(
            [new InstitutionCredential("item-1", "access-1", "Redwood Bank")], default);

        Assert.Equal(1, added);
        var institutions = await service.GetInstitutionsAsync(default);
        var restored = Assert.Single(institutions);
        Assert.Equal("access-1", restored.AccessToken);
        Assert.Equal(WorkStatus.Pending, restored.RefreshStatus);
        Assert.Null(restored.NextAttemptAtUtc); // due now, so the next refresh refills balances
    }

    [Fact]
    public async Task RestoreInstitutions_SkipsItemsAlreadyPresent()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        await service.AddInstitutionAsync("item-1", "access-1", "Redwood Bank", default);

        var added = await service.RestoreInstitutionsAsync(
            [
                new InstitutionCredential("item-1", "stale-token", "Redwood Bank"), // already present — skipped
                new InstitutionCredential("item-2", "access-2", "PNC")
            ], default);

        Assert.Equal(1, added);
        var institutions = await service.GetInstitutionsAsync(default);
        Assert.Equal(2, institutions.Count);
        // The existing Item keeps its live token — restore never overwrites a present Item.
        Assert.Equal("access-1", institutions.Single(i => i.ItemId == "item-1").AccessToken);
    }

    [Fact]
    public async Task RestoreInstitutions_IgnoresBlankCredentials()
    {
        using var db = new TestDatabase();
        var service = Create(db);

        var added = await service.RestoreInstitutionsAsync(
            [
                new InstitutionCredential("", "access", "NoId"),
                new InstitutionCredential("item-x", "", "NoToken")
            ], default);

        Assert.Equal(0, added);
        Assert.Empty(await service.GetInstitutionsAsync(default));
    }
}
