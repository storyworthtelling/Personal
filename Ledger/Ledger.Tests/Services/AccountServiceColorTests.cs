using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Index.Domain.External;
using Index.Domain.Pipeline;
using Index.Infrastructure.Services;
using Index.Tests.Support;

namespace Index.Tests.Services;

// Task 1.3 at link time: the colour is fetched right after the bank is recorded, from the institution
// id the Hosted Link session reported, and a failed lookup never undoes the connection.
public class AccountServiceColorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private const string PncColour = "plaid-primary-colour"; // opaque: PostItTint, not this, parses it

    private static AccountService Create(TestDatabase db) => new(db, new FakeClock(Now));

    [Fact]
    public async Task AddInstitution_RecordsThePlaidInstitutionIdFromTheLinkSession()
    {
        using var db = new TestDatabase();
        var service = Create(db);

        await service.AddInstitutionAsync("item-1", "access-1", "PNC", default, plaidInstitutionId: "ins_13");

        Assert.Equal("ins_13", Assert.Single(await service.GetInstitutionsAsync(default)).PlaidInstitutionId);
    }

    [Fact]
    public async Task LookUpColour_StoresIt_AndRecordsTheAnswer()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var added = await service.AddInstitutionAsync("item-1", "access-1", "PNC", default, "ins_13");
        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetInstitutionAsync("ins_13", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaidInstitution("ins_13", "PNC Bank", PncColour));

        await service.LookUpInstitutionColorAsync(added.Id, plaid.Object, NullLogger.Instance, default);

        var stored = Assert.Single(await service.GetInstitutionsAsync(default));
        Assert.Equal(PncColour, stored.PrimaryColorHex);
        Assert.Equal(Now, stored.ColorLookedUpAtUtc);
    }

    [Fact]
    public async Task LookUpColour_WhenPlaidFails_DoesNotThrow_AndLeavesTheBankUnmarked()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var added = await service.AddInstitutionAsync("item-1", "access-1", "PNC", default, "ins_13");
        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetInstitutionAsync("ins_13", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RateLimitException("Plaid rate limited: 429"));

        await service.LookUpInstitutionColorAsync(added.Id, plaid.Object, NullLogger.Instance, default);

        var stored = Assert.Single(await service.GetInstitutionsAsync(default));
        Assert.Null(stored.PrimaryColorHex);
        Assert.Null(stored.ColorLookedUpAtUtc);
    }

    [Fact]
    public async Task LookUpColour_ForAnUnknownInstitution_DoesNothing()
    {
        using var db = new TestDatabase();
        var plaid = new Mock<IPlaidClient>();

        await Create(db).LookUpInstitutionColorAsync(Guid.NewGuid(), plaid.Object, NullLogger.Instance, default);

        plaid.VerifyNoOtherCalls();
    }
}
