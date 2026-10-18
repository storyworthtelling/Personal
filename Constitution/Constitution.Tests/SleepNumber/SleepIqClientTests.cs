using System.Net;
using System.Text.Json;
using Constitution.Domain.Credentials;
using Constitution.Infrastructure.SleepNumber;
using Storyworthtelling.Voice.Health;

namespace Constitution.Tests.SleepNumber;

/// <summary>
/// C-7.2.1 — the SleepIQ client, against invented replies in the shape the client assumes (the spike was
/// skipped, D97). These pin the assumption, so a real device run shows exactly where it was wrong.
/// </summary>
public class SleepIqClientTests
{
    private static readonly DateOnly Morning = new(2026, 10, 17);

    [Fact]
    public async Task SignsInWithTheVaultLogin()
    {
        var bed = Bed.Working();
        var log = new CapturingSleepLog();

        var outcome = await Bed.Client(bed, log).SignInAsync(default);

        Assert.Equal(SleepIqFailure.None, outcome);
        var request = Assert.Single(bed.Sent);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/rest/login", request.Path);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(Bed.Login, body.RootElement.GetProperty("login").GetString());
        Assert.Equal(Bed.Word(), body.RootElement.GetProperty("password").GetString());
        Assert.Equal(["sleepiq sign-in: 200"], log.Lines);
    }

    [Fact]
    public async Task MapsTheSleepersSide()
    {
        var log = new CapturingSleepLog();

        var result = await Bed.Client(Bed.Working(), log).SleepersAsync(default);

        Assert.True(result.IsOk);
        var sleepers = result.Value!;
        Assert.Equal([BedSide.Left, BedSide.Right], sleepers.Select(s => s.Side));
        Assert.Equal([Bed.LeftId, Bed.RightId], sleepers.Select(s => s.SleeperId));
        Assert.Contains("sleepiq sleepers: 200", log.Lines);
    }

    [Fact]
    public async Task ReadsSideNamesAsWellAsNumbers()
    {
        var reply = "{\"sleepers\":[{\"sleeperId\":\"a\",\"side\":\"right\"},{\"sleeperId\":\"b\",\"side\":\"LEFT\"}]}";
        var bed = Bed.Working().On("sleeper", Bed.Reply(HttpStatusCode.OK, reply));

        var result = await Bed.Client(bed, new CapturingSleepLog()).SleepersAsync(default);

        Assert.Equal([BedSide.Right, BedSide.Left], result.Value!.Select(s => s.Side));
    }

    [Fact]
    public async Task AnInactiveSleeperIsLeftOut()
    {
        var reply = "{\"sleepers\":[{\"sleeperId\":\"a\",\"side\":0,\"active\":false},{\"sleeperId\":\"b\",\"side\":0,\"active\":true}]}";
        var bed = Bed.Working().On("sleeper", Bed.Reply(HttpStatusCode.OK, reply));

        var result = await Bed.Client(bed, new CapturingSleepLog()).SleepersAsync(default);

        Assert.Equal(["b"], result.Value!.Select(s => s.SleeperId));
    }

    [Fact]
    public async Task LaterCallsCarryTheSessionKey()
    {
        var bed = Bed.Working();

        await Bed.Client(bed, new CapturingSleepLog()).SleepersAsync(default);

        var list = bed.Sent.Single(request => request.Path == "/rest/sleeper");
        Assert.Equal(HttpMethod.Get, list.Method);
        Assert.Contains("_k=" + Bed.SessionKey, list.Query, StringComparison.Ordinal);
        Assert.Equal(1, bed.CallsTo("login"));
    }

    [Fact]
    public async Task ASessionIsReusedAcrossCalls()
    {
        var bed = Bed.Working().On("sleepData", Bed.Reply(HttpStatusCode.OK, Bed.SleepData("2026-10-17", 25200)));
        var client = Bed.Client(bed, new CapturingSleepLog());

        await client.SleepersAsync(default);
        await client.NightAsync(BedSide.Left, Morning, default);

        Assert.Equal(1, bed.CallsTo("login"));
    }

    [Fact]
    public async Task ReadsTheNightForTheSide()
    {
        var bed = Bed.Working().On("sleepData", Bed.Reply(HttpStatusCode.OK, Bed.SleepData("2026-10-17", 25200)));
        var log = new CapturingSleepLog();

        var result = await Bed.Client(bed, log).NightAsync(BedSide.Left, Morning, default);

        Assert.True(result.IsOk);
        Assert.Equal(new BedNight(Morning, TimeSpan.FromHours(7)), result.Value);
        var ask = bed.Sent.Single(request => request.Path == "/rest/sleepData");
        Assert.Contains("sleeper=" + Bed.LeftId, ask.Query, StringComparison.Ordinal);
        Assert.Contains("date=2026-10-17", ask.Query, StringComparison.Ordinal);
        Assert.Contains("sleepiq sleep-data: 200", log.Lines);
    }

    [Fact]
    public async Task ARightSideNightAsksForTheRightSleeper()
    {
        var bed = Bed.Working().On("sleepData", Bed.Reply(HttpStatusCode.OK, Bed.SleepData("2026-10-17", 3600)));

        await Bed.Client(bed, new CapturingSleepLog()).NightAsync(BedSide.Right, Morning, default);

        var ask = bed.Sent.Single(request => request.Path == "/rest/sleepData");
        Assert.Contains("sleeper=" + Bed.RightId, ask.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyBedNightIsNoReading()
    {
        var bed = Bed.Working().On("sleepData", Bed.Reply(HttpStatusCode.OK, Bed.SleepData("2026-10-17", 0)));

        var result = await Bed.Client(bed, new CapturingSleepLog()).NightAsync(BedSide.Left, Morning, default);

        Assert.True(result.IsOk);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ANightWithNoDayRecordIsNoReading()
    {
        var bed = Bed.Working().On("sleepData", Bed.Reply(HttpStatusCode.OK, Bed.SleepData("2026-10-16", 25200)));

        var result = await Bed.Client(bed, new CapturingSleepLog()).NightAsync(BedSide.Left, Morning, default);

        Assert.True(result.IsOk);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ASideNobodySleepsOnIsNoSuchSide()
    {
        var bed = Bed.Working().On("sleeper", Bed.Reply(HttpStatusCode.OK, Bed.LeftOnly()));
        var log = new CapturingSleepLog();

        var result = await Bed.Client(bed, log).NightAsync(BedSide.Right, Morning, default);

        Assert.Equal(SleepIqFailure.NoSuchSide, result.Failure);
        Assert.Equal(0, bed.CallsTo("sleepData"));
        Assert.Contains("sleepiq side: none on the right", log.Lines);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARefusedLoginIsSignInRejected(HttpStatusCode status)
    {
        var bed = Bed.Working().On("login", Bed.Reply(status));
        var log = new CapturingSleepLog();

        var outcome = await Bed.Client(bed, log).SignInAsync(default);

        Assert.Equal(SleepIqFailure.SignInRejected, outcome);
        Assert.Equal([$"sleepiq sign-in: {(int)status}"], log.Lines);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task ServerTroubleIsOffline(HttpStatusCode status)
    {
        var bed = Bed.Working().On("login", Bed.Reply(status));

        var outcome = await Bed.Client(bed, new CapturingSleepLog()).SignInAsync(default);

        Assert.Equal(SleepIqFailure.Offline, outcome);
    }

    [Fact]
    public async Task AMissingEndpointIsUnexpectedShape()
    {
        var bed = Bed.Working().On("sleeper", Bed.Reply(HttpStatusCode.NotFound));

        var result = await Bed.Client(bed, new CapturingSleepLog()).SleepersAsync(default);

        Assert.Equal(SleepIqFailure.UnexpectedShape, result.Failure);
    }

    [Fact]
    public async Task ADroppedConnectionIsOffline()
    {
        var bed = Bed.Working().On("login", Bed.Fail(new HttpRequestException("example connection dropped")));
        var log = new CapturingSleepLog();

        var result = await Bed.Client(bed, log).SleepersAsync(default);

        Assert.Equal(SleepIqFailure.Offline, result.Failure);
        Assert.Contains("sleepiq sign-in: offline (HttpRequestException)", log.Lines);
    }

    [Fact]
    public async Task ATimeoutIsOffline()
    {
        var bed = Bed.Working().On("sleeper", Bed.Fail(new TaskCanceledException("example timeout")));

        var result = await Bed.Client(bed, new CapturingSleepLog()).SleepersAsync(default);

        Assert.Equal(SleepIqFailure.Offline, result.Failure);
    }

    [Fact]
    public async Task TheCallersOwnCancelIsNotOffline()
    {
        using var cancel = new CancellationTokenSource();
        var bed = Bed.Working().On("sleeper", () =>
        {
            cancel.Cancel();
            throw new TaskCanceledException("example cancel");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Bed.Client(bed, new CapturingSleepLog()).SleepersAsync(cancel.Token));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"key\":\"\"}")]
    [InlineData("{\"key\":7}")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    public async Task ASignInWithoutAKeyIsUnexpectedShape(string reply)
    {
        var bed = Bed.Working().On("login", Bed.Reply(HttpStatusCode.OK, reply));
        var log = new CapturingSleepLog();

        var outcome = await Bed.Client(bed, log).SignInAsync(default);

        Assert.Equal(SleepIqFailure.UnexpectedShape, outcome);
        Assert.Contains("sleepiq sign-in: 200", log.Lines);
        Assert.Contains(log.Lines, line => line.StartsWith("sleepiq sign-in: unexpected shape", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"sleepers\":{}}")]
    [InlineData("{\"sleepers\":[{\"side\":0}]}")]
    [InlineData("{\"sleepers\":[{\"sleeperId\":\"a\"}]}")]
    [InlineData("{\"sleepers\":[{\"sleeperId\":\"a\",\"side\":5}]}")]
    [InlineData("{\"sleepers\":[{\"sleeperId\":\"a\",\"side\":\"middle\"}]}")]
    [InlineData("<html>maintenance</html>")]
    public async Task AWrongSleeperListIsUnexpectedShape(string reply)
    {
        var bed = Bed.Working().On("sleeper", Bed.Reply(HttpStatusCode.OK, reply));
        var log = new CapturingSleepLog();

        var result = await Bed.Client(bed, log).SleepersAsync(default);

        Assert.Equal(SleepIqFailure.UnexpectedShape, result.Failure);
        Assert.Contains(log.Lines, line => line.StartsWith("sleepiq sleepers: unexpected shape", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"sleepData\":{}}")]
    [InlineData("{\"sleepData\":[{\"days\":{}}]}")]
    [InlineData("{\"sleepData\":[{\"days\":[{\"date\":\"2026-10-17\"}]}]}")]
    [InlineData("{\"sleepData\":[{\"days\":[{\"date\":\"2026-10-17\",\"totalSleepSessionTime\":\"lots\"}]}]}")]
    [InlineData("{\"sleepData\":[{\"days\":[{\"date\":\"2026-10-17\",\"totalSleepSessionTime\":-5}]}]}")]
    public async Task AWrongNightIsUnexpectedShape(string reply)
    {
        var bed = Bed.Working().On("sleepData", Bed.Reply(HttpStatusCode.OK, reply));
        var log = new CapturingSleepLog();

        var result = await Bed.Client(bed, log).NightAsync(BedSide.Left, Morning, default);

        Assert.Equal(SleepIqFailure.UnexpectedShape, result.Failure);
        Assert.Contains(log.Lines, line => line.StartsWith("sleepiq sleep-data: unexpected shape", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnExpiredKeySignsInOnceMore()
    {
        var bed = Bed.Working()
            .On("sleepData", Bed.Reply(HttpStatusCode.Unauthorized), Bed.Reply(HttpStatusCode.OK, Bed.SleepData("2026-10-17", 25200)));
        var log = new CapturingSleepLog();

        var result = await Bed.Client(bed, log).NightAsync(BedSide.Left, Morning, default);

        Assert.True(result.IsOk);
        Assert.Equal(TimeSpan.FromHours(7), result.Value!.Asleep);
        Assert.Equal(2, bed.CallsTo("login"));
        Assert.Equal(2, bed.CallsTo("sleepData"));
    }

    [Fact]
    public async Task ARefusalAfterAFreshSignInIsFinal()
    {
        var bed = Bed.Working().On("sleepData", Bed.Reply(HttpStatusCode.Forbidden));

        var result = await Bed.Client(bed, new CapturingSleepLog()).NightAsync(BedSide.Left, Morning, default);

        Assert.Equal(SleepIqFailure.SignInRejected, result.Failure);
        Assert.Equal(2, bed.CallsTo("sleepData"));
    }

    [Theory]
    [InlineData("", "example-word")]
    [InlineData("sleeper@example.com", "")]
    [InlineData("  ", "  ")]
    public async Task NoLoginMakesNoCall(string login, string word)
    {
        var bed = Bed.Working();
        var log = new CapturingSleepLog();

        var outcome = await Bed.Client(bed, log, Bed.Connections(login, word)).SignInAsync(default);

        Assert.Equal(SleepIqFailure.NoLogin, outcome);
        Assert.Empty(bed.Sent);
        Assert.Equal(["sleepiq sign-in: no login set up"], log.Lines);
    }

    [Fact]
    public async Task OnlyARejectedLoginFlagsTheCredential()
    {
        var sink = new FakeSink();
        var bed = Bed.Working().On("login", Bed.Reply(HttpStatusCode.Unauthorized));
        var client = new SleepIqClient(SleepIqClient.CreateHttpClient(bed, sink), Bed.VaultLogin(), new CapturingSleepLog());

        await client.SignInAsync(default);

        var failure = Assert.Single(sink.Failing);
        Assert.Equal(CredentialIds.SleepNumber, failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task TroubleThatIsNotARefusalFlagsNothing(HttpStatusCode status)
    {
        var sink = new FakeSink();
        var bed = Bed.Working().On("login", Bed.Reply(status));
        var client = new SleepIqClient(SleepIqClient.CreateHttpClient(bed, sink), Bed.VaultLogin(), new CapturingSleepLog());

        await client.SignInAsync(default);

        Assert.Empty(sink.Failing);
    }

    [Fact]
    public async Task ADroppedConnectionFlagsNothing()
    {
        var sink = new FakeSink();
        var bed = Bed.Working().On("login", Bed.Fail(new HttpRequestException("example connection dropped")));
        var client = new SleepIqClient(SleepIqClient.CreateHttpClient(bed, sink), Bed.VaultLogin(), new CapturingSleepLog());

        await client.SignInAsync(default);

        Assert.Empty(sink.Failing);
        Assert.Empty(sink.Working);
    }

    [Fact]
    public async Task AWorkingLoginMarksTheCredentialOk()
    {
        var sink = new FakeSink();
        var client = new SleepIqClient(SleepIqClient.CreateHttpClient(Bed.Working(), sink), Bed.VaultLogin(), new CapturingSleepLog());

        await client.SignInAsync(default);

        Assert.Equal([CredentialIds.SleepNumber], sink.Working);
    }

    private sealed class FakeSink : ICredentialHealthSink
    {
        public List<string> Working { get; } = [];
        public List<string> Failing { get; } = [];
        public void MarkOk(string credentialId) => Working.Add(credentialId);
        public void MarkFailing(string credentialId, string message) => Failing.Add(credentialId);
    }
}
