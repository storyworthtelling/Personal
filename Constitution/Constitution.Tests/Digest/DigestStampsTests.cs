using Constitution.Infrastructure.Documents;

namespace Constitution.Tests.DigestPages;

/// <summary>The per-week, per-adult stamp that makes the Saturday run idempotent survives a restart and a damaged file.</summary>
public class DigestStampsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "digest-stamps-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }

    private string File1 => Path.Combine(folder, "digest-stamps.json");

    [Fact]
    public void RoundTripsAcrossInstances()
    {
        new FileDigestStamps(File1).Mark("Sam", DigestFixture.Saturday);
        var again = new FileDigestStamps(File1);
        Assert.True(again.Has("Sam", DigestFixture.Saturday));
        Assert.False(again.Has("Lee", DigestFixture.Saturday));
        Assert.False(again.Has("Sam", DigestFixture.Saturday.AddDays(7)));
    }

    [Fact]
    public void PersonNamesIgnoreCase()
    {
        new FileDigestStamps(File1).Mark("Sam", DigestFixture.Saturday);
        Assert.True(new FileDigestStamps(File1).Has("sam", DigestFixture.Saturday));
    }

    [Fact]
    public void ADamagedFileMeansNothingStampedNotACrash()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(File1, "{ not json");
        var stamps = new FileDigestStamps(File1);
        Assert.False(stamps.Has("Sam", DigestFixture.Saturday));
        stamps.Mark("Sam", DigestFixture.Saturday);
        Assert.True(new FileDigestStamps(File1).Has("Sam", DigestFixture.Saturday));
    }

    [Fact]
    public void OnlyTheRecentWeeksAreKept()
    {
        var stamps = new FileDigestStamps(File1);
        for (var week = 0; week < 10; week++)
        {
            stamps.Mark("Sam", DigestFixture.Saturday.AddDays(7 * week));
        }

        var again = new FileDigestStamps(File1);
        Assert.False(again.Has("Sam", DigestFixture.Saturday));
        Assert.True(again.Has("Sam", DigestFixture.Saturday.AddDays(63)));
    }
}

/// <summary>The trail names each outcome in words and carries nothing of the page.</summary>
public class DigestTrailTests
{
    [Fact]
    public void EveryOutcomeHasValueFreeWords()
    {
        DigestOutcome[] all =
        [
            new DigestOutcome.NotDue(), new DigestOutcome.AlreadyDelivered(DigestFixture.Saturday), new DigestOutcome.NotReady("The skip ledger could not be read."),
            new DigestOutcome.Delivered("Constitution — Digest — 2026-03-14"), new DigestOutcome.NotDelivered("n", new HttpRequestException("x")),
        ];
        var phrases = all.Select(DigestTrail.LogPhrase).ToList();
        Assert.Equal(all.Length, phrases.Distinct().Count());
        Assert.All(phrases, phrase =>
        {
            Assert.DoesNotContain(phrase, char.IsDigit);
            Assert.DoesNotContain("Sam", phrase, StringComparison.Ordinal);
            Assert.DoesNotContain("ledger", phrase, StringComparison.Ordinal);
        });
    }
}
