using System.Net;
using Constitution.Infrastructure.SleepNumber;

namespace Constitution.Tests.SleepNumber;

/// <summary>
/// C-7.2.1 — nothing personal leaves the client except in a typed result: no name, no token, no
/// password, no id, no reading in a log line, a result's text or a failure.
/// </summary>
public class SleepIqPrivacyTests
{
    private static readonly DateOnly Morning = new(2026, 10, 17);

    private static string[] Secrets() =>
        [Bed.LeftName, Bed.RightName, Bed.SessionKey, Bed.UserId, Bed.Word(), Bed.Login, Bed.LeftId, Bed.RightId, "25200", "88"];

    [Fact]
    public async Task NothingPersonalReachesTheLog()
    {
        var bed = Bed.Working().On("sleepData", Bed.Reply(HttpStatusCode.OK, Bed.SleepData("2026-10-17", 25200)));
        var log = new CapturingSleepLog();
        var client = Bed.Client(bed, log);

        await client.SleepersAsync(default);
        await client.NightAsync(BedSide.Left, Morning, default);
        await client.NightAsync(BedSide.Right, Morning, default);

        Assert.NotEmpty(log.Lines);
        foreach (var secret in Secrets())
        {
            Assert.DoesNotContain(log.Lines, line => line.Contains(secret, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task NothingPersonalReachesTheLogOnFailure()
    {
        var log = new CapturingSleepLog();
        foreach (var bed in new[]
        {
            Bed.Working().On("login", Bed.Reply(HttpStatusCode.Unauthorized, Bed.SignedIn())),
            Bed.Working().On("login", Bed.Fail(new HttpRequestException($"example failure for {Bed.Login} key {Bed.SessionKey}"))),
            Bed.Working().On("sleeper", Bed.Reply(HttpStatusCode.OK, Bed.LeftOnly().Replace("sleepers", "others", StringComparison.Ordinal))),
            Bed.Working().On("sleepData", Bed.Reply(HttpStatusCode.OK, "{\"note\":\"" + Bed.LeftName + "\"}")),
        })
        {
            var client = Bed.Client(bed, log);
            await client.SignInAsync(default);
            await client.NightAsync(BedSide.Left, Morning, default);
        }

        Assert.NotEmpty(log.Lines);
        foreach (var secret in Secrets())
        {
            Assert.DoesNotContain(log.Lines, line => line.Contains(secret, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task NoResultTextHoldsAName()
    {
        var bed = Bed.Working().On("sleepData", Bed.Reply(HttpStatusCode.OK, Bed.SleepData("2026-10-17", 25200)));
        var client = Bed.Client(bed, new CapturingSleepLog());

        var sleepers = await client.SleepersAsync(default);
        var texts = new List<string> { sleepers.ToString() };
        texts.AddRange(sleepers.Value!.Select(sleeper => sleeper.ToString()));
        var failed = SleepIqResult<BedSleeper>.Failed(SleepIqFailure.Offline);
        texts.Add(failed.ToString());

        foreach (var name in new[] { Bed.LeftName, Bed.RightName })
        {
            Assert.DoesNotContain(texts, text => text.Contains(name, StringComparison.Ordinal));
        }

        Assert.DoesNotContain(texts, text => text.Contains(Bed.LeftId, StringComparison.Ordinal));
    }

    [Fact]
    public void AFailedResultNeedsARealFailure()
    {
        Assert.Throws<ArgumentException>(() => SleepIqResult<int>.Failed(SleepIqFailure.None));
    }

    [Fact]
    public void ASleeperHasNoMemberForAName()
    {
        var members = typeof(BedSleeper).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain(members, name => name.Contains("Name", StringComparison.OrdinalIgnoreCase));
    }
}
