using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Pipeline;
using Index.Infrastructure.Configuration;
using Index.Infrastructure.Pipeline;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Services;

// Error-handling §9: a bank disconnected while offline asked Plaid to release the Item, failed, logged it and
// forgot it. The local row holding the only access token was already gone, so the Item stayed live — and
// billed — at Plaid for good. A release that fails now waits in an outbox, keeping the token, and the refresh
// worker asks again until Plaid confirms.
public class PlaidReleaseOutboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    private static BalanceRefreshProcessor Processor(TestDatabase db, IPlaidClient plaid, FakeClock clock) =>
        new(db, plaid, clock, Options.Create(new RefreshOptions()), NullLogger<BalanceRefreshProcessor>.Instance);

    [Fact]
    public async Task AReleasePlaidRefusesWaitsWithItsTokenAndIsAskedAgainLater()
    {
        using var db = new TestDatabase();
        var clock = new FakeClock(Now);
        var service = new AccountService(db, clock);
        var bank = await service.AddInstitutionAsync("item-1", "access-1", "Redwood Bank", default);
        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.RemoveItemAsync("access-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection failure"));

        var disconnected = await service.DisconnectInstitutionAsync(bank.Id, default);
        Assert.False(await service.CommitDisconnectAsync(disconnected!, plaid.Object, NullLogger.Instance, default));

        await using (var context = db.CreateDbContext())
        {
            var waiting = Assert.Single(context.PendingPlaidReleases);
            Assert.Equal("access-1", waiting.AccessToken);
            Assert.Equal("Redwood Bank", waiting.InstitutionName);
        }

        // Plaid answers next time, once the wait is up.
        plaid.Reset();
        clock.Advance(TimeSpan.FromHours(2));
        var result = await Processor(db, plaid.Object, clock).ProcessNextAsync(default);

        Assert.Equal(ProcessOutcome.DidWork, result.Outcome);
        plaid.Verify(p => p.RemoveItemAsync("access-1", It.IsAny<CancellationToken>()), Times.Once);
        await using (var context = db.CreateDbContext())
        {
            Assert.Empty(context.PendingPlaidReleases);
        }
    }

    [Fact]
    public async Task AReleaseThatFailsAgainWaitsLongerAndKeepsItsToken()
    {
        using var db = new TestDatabase();
        var clock = new FakeClock(Now);
        await using (var context = db.CreateDbContext())
        {
            context.PendingPlaidReleases.Add(new PendingPlaidRelease
            {
                ItemId = "item-1", AccessToken = "access-1", InstitutionName = "Redwood Bank",
                QueuedAtUtc = Now, Attempts = 1, NextAttemptAtUtc = Now,
            });
            await context.SaveChangesAsync();
        }
        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.RemoveItemAsync("access-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503"));

        await Processor(db, plaid.Object, clock).ProcessNextAsync(default);

        await using (var context = db.CreateDbContext())
        {
            var waiting = Assert.Single(context.PendingPlaidReleases);
            Assert.Equal(2, waiting.Attempts);
            Assert.True(waiting.NextAttemptAtUtc > Now);
            Assert.Equal("access-1", waiting.AccessToken);
        }
    }

    [Fact]
    public async Task AnItemPlaidNoLongerKnowsIsReleasedAlready()
    {
        using var db = new TestDatabase();
        var clock = new FakeClock(Now);
        await using (var context = db.CreateDbContext())
        {
            context.PendingPlaidReleases.Add(new PendingPlaidRelease
            {
                ItemId = "item-1", AccessToken = "access-1", InstitutionName = "Redwood Bank",
                QueuedAtUtc = Now, Attempts = 1, NextAttemptAtUtc = Now,
            });
            await context.SaveChangesAsync();
        }
        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.RemoveItemAsync("access-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("ITEM_NOT_FOUND: the Item you requested cannot be found"));

        await Processor(db, plaid.Object, clock).ProcessNextAsync(default);

        await using var after = db.CreateDbContext();
        Assert.Empty(after.PendingPlaidReleases);
    }

    [Fact]
    public async Task AReleaseThatWorksTheFirstTimeLeavesNothingWaiting()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));
        var bank = await service.AddInstitutionAsync("item-1", "access-1", "Redwood Bank", default);
        var plaid = new Mock<IPlaidClient>();

        var disconnected = await service.DisconnectInstitutionAsync(bank.Id, default);
        Assert.True(await service.CommitDisconnectAsync(disconnected!, plaid.Object, NullLogger.Instance, default));

        await using var context = db.CreateDbContext();
        Assert.Empty(context.PendingPlaidReleases);
    }
}
