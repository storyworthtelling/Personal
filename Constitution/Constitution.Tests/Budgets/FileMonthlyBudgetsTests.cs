using Constitution.Domain.Budgets;
using Constitution.Infrastructure.Budgets;

namespace Constitution.Tests.Budgets;

public sealed class FileMonthlyBudgetsTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly string folder = Directory.CreateTempSubdirectory("constitution-budgets").FullName;

    private string Path => System.IO.Path.Combine(folder, FileMonthlyBudgets.FileName);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static BudgetPayload Payload(string month, string budget, string spent) =>
        BudgetPayload.Parse(key => key switch { "month" => month, "budget" => budget, _ => spent }).Payload!;

    [Fact]
    public void NoFileMeansNothingKnown()
    {
        Assert.Empty(new FileMonthlyBudgets(Path).Load().Entries);
        Assert.False(File.Exists(Path));
    }

    [Fact]
    public void WhatWasReceivedIsThereAfterARestart()
    {
        new FileMonthlyBudgets(Path).Receive(Payload("2026-10", "600.00", "412.50"), Noon);

        var entry = Assert.Single(new FileMonthlyBudgets(Path).Load().Entries);
        Assert.Equal(new BudgetEntry("2026-10", 600.00m, 412.50m, Noon), entry);
    }

    [Fact]
    public void TheLatestPerMonthSurvivesAnOlderArrival()
    {
        var store = new FileMonthlyBudgets(Path);
        store.Receive(Payload("2026-10", "650.00", "412.50"), Noon);
        store.Receive(Payload("2026-10", "600.00", "100.00"), Noon.AddMinutes(-3));
        store.Receive(Payload("2026-09", "590.00", "580.00"), Noon.AddMinutes(-90));

        var kept = new FileMonthlyBudgets(Path).Load();
        Assert.Equal(412.50m, kept.Find("2026-10")!.Spent);
        Assert.Equal(580.00m, kept.Find("2026-09")!.Spent);
    }

    [Fact]
    public void AnUnreadableFileIsSetAsideWithItsBytes()
    {
        File.WriteAllText(Path, "{ torn");

        var kept = new FileMonthlyBudgets(Path).Load();

        Assert.Empty(kept.Entries);
        Assert.Equal("{ torn", File.ReadAllText(Path + ".unreadable"));
        Assert.False(File.Exists(Path));
    }

    [Fact]
    public void ReceivingAfterDamageNeverWritesOverTheDamagedBytes()
    {
        File.WriteAllText(Path, "{ torn");

        new FileMonthlyBudgets(Path).Receive(Payload("2026-10", "600.00", "412.50"), Noon);

        Assert.Equal("{ torn", File.ReadAllText(Path + ".unreadable"));
        Assert.Equal(412.50m, new FileMonthlyBudgets(Path).Load().Find("2026-10")!.Spent);
    }

    [Fact]
    public void ASecondDamagedFileNeverReplacesTheFirstCopy()
    {
        File.WriteAllText(Path, "first damage");
        _ = new FileMonthlyBudgets(Path).Load();
        File.WriteAllText(Path, "second damage");
        _ = new FileMonthlyBudgets(Path).Load();

        Assert.Equal("first damage", File.ReadAllText(Path + ".unreadable"));
        Assert.Equal("second damage", File.ReadAllText(Path + ".unreadable.2"));
    }

    [Fact]
    public void AFileWithOneBadEntryIsNotHalfRead()
    {
        File.WriteAllText(Path, "{\"version\":1,\"months\":[{\"month\":\"2026-10\",\"budget\":\"-1\",\"spent\":\"1.00\",\"receivedAt\":\"2026-10-24T12:00:00.0000000+00:00\"}]}");

        Assert.Empty(new FileMonthlyBudgets(Path).Load().Entries);
        Assert.True(File.Exists(Path + ".unreadable"));
    }
}
