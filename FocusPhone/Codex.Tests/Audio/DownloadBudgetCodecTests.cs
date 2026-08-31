using Codex.Domain.Audio;
using Codex.Infrastructure.Podcasts;
using Xunit;

namespace Codex.Tests.Audio;

public class DownloadBudgetCodecTests
{
    private static readonly DateTimeOffset Taken = new(2026, 8, 20, 7, 30, 0, TimeSpan.Zero);

    [Fact]
    public void RoundTrips()
    {
        var budget = DownloadBudget.Empty
            .With(new DownloadedItem("song", "A Song", DownloadKind.Music, TimeSpan.FromMinutes(4), Taken))
            .With(new DownloadedItem("episode", "An Episode", DownloadKind.Podcast, TimeSpan.FromMinutes(52), Taken));

        var back = DownloadBudgetCodec.Decode(DownloadBudgetCodec.Encode(budget));

        // Field by field: a record whose members are collections compares by
        // reference, so two identical budgets are never Equal.
        Assert.Equal(2, back.Items.Count);
        Assert.Equal(budget.Used, back.Used);
        Assert.Equal(DownloadKind.Podcast, back.Items.Single(item => item.MediaId == "episode").Kind);
        Assert.Equal(Taken, back.Items[0].DownloadedUtc);
        Assert.Equal("An Episode", back.Items.Single(item => item.MediaId == "episode").Title);
    }

    // The dates are the whole point of persisting this: without them the sweep has no
    // idea how long anything has been held.
    [Fact]
    public void KeepsTheDownloadDateToTheMinute()
    {
        var budget = DownloadBudget.Empty
            .With(new DownloadedItem("song", "A Song", DownloadKind.Music, TimeSpan.FromMinutes(4), Taken));

        var back = DownloadBudgetCodec.Decode(DownloadBudgetCodec.Encode(budget));

        Assert.Equal(DownloadBudget.LifeDays - 8, back.DaysLeft("song", Taken.AddDays(8)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Items\":\"wrong shape\"}")]
    public void Decode_FallsBackToEmpty(string? json)
    {
        Assert.Empty(DownloadBudgetCodec.Decode(json).Items);
    }

    // Written as names, because this file outlives the code that wrote it and an enum
    // value inserted later would silently re-map every stored kind.
    [Fact]
    public void Encode_WritesKindsAsNames()
    {
        var budget = DownloadBudget.Empty
            .With(new DownloadedItem("episode", "An Episode", DownloadKind.Podcast, TimeSpan.FromMinutes(1), Taken));

        Assert.Contains("Podcast", DownloadBudgetCodec.Encode(budget));
    }
}
