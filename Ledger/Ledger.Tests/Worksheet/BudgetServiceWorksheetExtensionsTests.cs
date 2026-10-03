using Index.Domain.External;
using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Moq;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>Release F's own extension of <see cref="BudgetService"/> (task brief: "extend, not
/// rewrite"): recording a worksheet's delivery, the income correction, and the D66 status stage that
/// follows a read-back.</summary>
public class BudgetServiceWorksheetExtensionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static BudgetService Create(TestDatabase db) =>
        new(db, new FakeClock(Now), new Mock<IPlaidClient>().Object);

    [Fact]
    public async Task RecordWorksheetDelivery_SetsStatusAndTheExactPrintedCategoryOrder()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var groceries = await service.AddCategoryAsync("Groceries", default);
        var dining = await service.AddCategoryAsync("Dining out", default);
        var month = await service.GetOrCreateMonthAsync("2026-10", default);

        await service.RecordWorksheetDeliveryAsync(month.Id, [groceries.Id, dining.Id], default);

        var updated = await service.FindMonthAsync("2026-10", default);
        Assert.Equal(BudgetMonthStatus.DeliveredToRemarkable, updated!.Status);

        var order = await service.GetWorksheetCategoryOrderAsync(month.Id, default);
        Assert.Equal([groceries.Id, dining.Id], order);
    }

    [Fact]
    public async Task GetWorksheetCategoryOrder_IsNull_WhenNeverDelivered()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var month = await service.GetOrCreateMonthAsync("2026-10", default);

        Assert.Null(await service.GetWorksheetCategoryOrderAsync(month.Id, default));
    }

    [Fact]
    public async Task SetIncomeOverride_WinsForTheMonth()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var month = await service.GetOrCreateMonthAsync("2026-10", default);

        await service.SetIncomeOverrideAsync(month.Id, 11_000m, default);

        var updated = await service.FindMonthAsync("2026-10", default);
        Assert.Equal(11_000m, updated!.IncomeOverride);
    }

    [Fact]
    public async Task MarkAwaitingConfirmation_MovesTheStatusForward()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var month = await service.GetOrCreateMonthAsync("2026-10", default);
        await service.RecordWorksheetDeliveryAsync(month.Id, [], default);

        await service.MarkAwaitingConfirmationAsync(month.Id, default);

        var updated = await service.FindMonthAsync("2026-10", default);
        Assert.Equal(BudgetMonthStatus.AwaitingConfirmation, updated!.Status);
    }
}
