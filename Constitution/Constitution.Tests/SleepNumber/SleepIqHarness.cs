using System.Net;
using System.Text;
using Constitution.Infrastructure.SleepNumber;
using Constitution.Infrastructure.Vault;
using Moq;

namespace Constitution.Tests.SleepNumber;

/// <summary>One request the client made, captured as it was sent.</summary>
internal sealed record SentRequest(HttpMethod Method, string Path, string Query, string Body);

/// <summary>
/// A pretend SleepIQ service. Responses are scripted per endpoint (the last path segment: login, sleeper,
/// sleepData) and the last one repeats. Nothing here is a real response; it is the shape the client assumes.
/// </summary>
internal sealed class FakeBed : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> script = new(StringComparer.Ordinal);

    public List<SentRequest> Sent { get; } = [];

    public FakeBed On(string endpoint, params Func<HttpResponseMessage>[] replies)
    {
        script[endpoint] = new Queue<Func<HttpResponseMessage>>(replies);
        return this;
    }

    public int CallsTo(string endpoint) => Sent.Count(request => request.Path.EndsWith("/" + endpoint, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var uri = request.RequestUri!;
        Sent.Add(new SentRequest(request.Method, uri.AbsolutePath, uri.Query, body));

        var endpoint = uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..];
        if (!script.TryGetValue(endpoint, out var replies) || replies.Count == 0)
        {
            throw new InvalidOperationException("The test scripted no reply for " + endpoint);
        }

        var next = replies.Count > 1 ? replies.Dequeue() : replies.Peek();
        return next();
    }
}

internal sealed class CapturingSleepLog : ISleepIqLog
{
    public List<string> Lines { get; } = [];

    public void Outcome(string phrase) => Lines.Add(phrase);
}

internal sealed class FixedChoice : ISleeperChoice
{
    public FixedChoice(BedSide? side) => Side = side;

    public BedSide? Side { get; }
}

/// <summary>The invented values and replies every SleepIQ test uses. None resembles a real one.</summary>
internal static class Bed
{
    public const string Login = "sleeper@example.com";
    public const string SessionKey = "example-session-key";
    public const string UserId = "example-user-id";
    public const string LeftId = "example-sleeper-left";
    public const string RightId = "example-sleeper-right";

    // Names the service would send. They must never come out the other side.
    public const string LeftName = "Zephyrine";
    public const string RightName = "Quillon";

    /// <summary>Built at run time so no password-shaped literal sits in source.</summary>
    public static string Word() => $"{"example-bed"}{"-word"}";

    public static IConstitutionConnections Connections(string login, string word)
    {
        var connections = new Mock<IConstitutionConnections>();
        connections.SetupGet(c => c.SleepNumberLogin).Returns(login);
        connections.SetupGet(c => c.SleepNumberPassword).Returns(word);
        return connections.Object;
    }

    public static IConstitutionConnections VaultLogin() => Connections(Login, Word());

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static Func<HttpResponseMessage> Reply(HttpStatusCode status, string json = "{}") => () => Json(status, json);

    public static Func<HttpResponseMessage> Fail(Exception error) => () => throw error;

    public static string SignedIn() => $"{{\"key\":\"{SessionKey}\",\"userId\":\"{UserId}\"}}";

    public static string Sleepers() =>
        "{\"sleepers\":["
        + $"{{\"sleeperId\":\"{LeftId}\",\"bedId\":\"example-bed-id\",\"side\":0,\"firstName\":\"{LeftName}\",\"active\":true}},"
        + $"{{\"sleeperId\":\"{RightId}\",\"bedId\":\"example-bed-id\",\"side\":1,\"firstName\":\"{RightName}\",\"active\":true}}"
        + "]}";

    public static string LeftOnly() =>
        $"{{\"sleepers\":[{{\"sleeperId\":\"{LeftId}\",\"bedId\":\"example-bed-id\",\"side\":0,\"firstName\":\"{LeftName}\"}}]}}";

    /// <summary>Seven hours is 25200 seconds.</summary>
    public static string SleepData(string date, long seconds) =>
        $"{{\"sleepData\":[{{\"sleeperId\":\"{LeftId}\",\"days\":[{{\"date\":\"{date}\",\"totalSleepSessionTime\":{seconds},\"sleepQuality\":88}}]}}]}}";

    public static SleepIqClient Client(FakeBed fake, CapturingSleepLog log, IConstitutionConnections? connections = null)
    {
        var http = SleepIqClient.CreateHttpClient(fake, sink: null);
        return new SleepIqClient(http, connections ?? VaultLogin(), log);
    }

    /// <summary>A fake bed that signs in and lists both sleepers, ready for a night to be scripted.</summary>
    public static FakeBed Working() => new FakeBed()
        .On("login", Reply(HttpStatusCode.OK, SignedIn()))
        .On("sleeper", Reply(HttpStatusCode.OK, Sleepers()));
}
