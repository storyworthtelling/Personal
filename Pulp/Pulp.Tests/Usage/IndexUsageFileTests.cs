using System.Text.Json;
using Pulp.Domain.Core;
using Pulp.Domain.Services;
using Pulp.Domain.Usage;

namespace Pulp.Tests.Usage;

/// <summary>
/// The usage file Pulp leaves in Drive for an Index install that Pulp never ran beside (D320): the same
/// fields the broadcast carries and no others, replaced per service-month, zero removing. The wire shape
/// is pinned here and read back out of Index's own codec by content (Guard G16, the file's half).
/// </summary>
public class IndexUsageFileTests
{
    private static readonly ServiceCatalogue Catalogue = ServiceCatalogue.Public;

    private static IndexUsagePayload Payload(string service, int year, int month, int count, string? last) =>
        new(new ServiceId(service), new UsageMonth(year, month), count, last is null ? null : DateOnly.Parse(last));

    [Fact]
    public void TheFileNameIsFrozen() =>
        Assert.Equal("storyworthtelling-pulp-usage.json", IndexUsageFile.FileName);

    [Fact]
    public void AnEntryCarriesExactlyTheBroadcastsFieldsAndNothingElse()
    {
        var entries = IndexUsageFile.Apply([], [Payload("netflix", 2026, 9, 14, "2026-09-28")], Catalogue);

        using var json = JsonDocument.Parse(IndexUsageFile.Serialize(entries, Catalogue));
        var entry = json.RootElement.GetProperty("entries")[0];

        Assert.Equal(
            ["count", "lastOpened", "merchants", "month", "service"],
            entry.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToList());
        Assert.Equal("netflix", entry.GetProperty("service").GetString());
        Assert.Equal("2026-09", entry.GetProperty("month").GetString());
        Assert.Equal(14, entry.GetProperty("count").GetInt32());
        Assert.Equal("2026-09-28", entry.GetProperty("lastOpened").GetString());
        Assert.Contains("Netflix", entry.GetProperty("merchants").EnumerateArray().Select(name => name.GetString()));
    }

    [Fact]
    public void ADatelessEntryOmitsTheDayRatherThanWritingAnEmptyOne()
    {
        var entries = IndexUsageFile.Apply([], [Payload("hulu", 2026, 9, 2, null)], Catalogue);

        using var json = JsonDocument.Parse(IndexUsageFile.Serialize(entries, Catalogue));

        Assert.False(json.RootElement.GetProperty("entries")[0].TryGetProperty("lastOpened", out _));
    }

    [Fact]
    public void ATouchedServiceMonthReplacesItsEarlierTotalAndLeavesTheOthers()
    {
        var first = IndexUsageFile.Apply([], [Payload("netflix", 2026, 8, 3, "2026-08-10"), Payload("netflix", 2026, 9, 4, "2026-09-02")], Catalogue);

        var next = IndexUsageFile.Apply(first, [Payload("netflix", 2026, 9, 5, "2026-09-03")], Catalogue);

        Assert.Equal([("netflix", "2026-08", 3), ("netflix", "2026-09", 5)], next.Select(entry => (entry.Service.Value, entry.Month.ToString(), entry.Count)));
    }

    [Fact]
    public void ACountOfZeroRemovesTheMonthInsteadOfStandingAsAStaleNumber()
    {
        var first = IndexUsageFile.Apply([], [Payload("netflix", 2026, 9, 1, "2026-09-02")], Catalogue);

        Assert.Empty(IndexUsageFile.Apply(first, [Payload("netflix", 2026, 9, 0, null)], Catalogue));
    }

    [Fact]
    public void AServiceTheCatalogueLacksIsNotWritten()
    {
        Assert.Empty(IndexUsageFile.Apply([], [Payload("not-a-real-service", 2026, 9, 3, null)], Catalogue));
    }

    [Fact]
    public void WhatIsSerializedReadsBackIdentical()
    {
        var entries = IndexUsageFile.Apply([], [Payload("netflix", 2026, 9, 14, "2026-09-28"), Payload("hulu", 2026, 9, 2, null)], Catalogue);

        Assert.True(IndexUsageFile.TryRead(IndexUsageFile.Serialize(entries, Catalogue), out var read));
        Assert.Equal(entries, read);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"entries\":[{\"service\":\"netflix\",\"month\":\"September\",\"count\":3,\"merchants\":[\"Netflix\"]}]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"service\":\"netflix\",\"month\":\"2026-09\",\"count\":-1,\"merchants\":[\"Netflix\"]}]}")]
    [InlineData("{\"version\":2,\"entries\":[]}")]
    public void AGarbledOrNewerFileIsNotReadRatherThanGuessed(string text)
    {
        Assert.False(IndexUsageFile.TryRead(text, out _));
    }
}
