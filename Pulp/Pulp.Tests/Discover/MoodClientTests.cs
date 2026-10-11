using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.Friday;
using Pulp.Infrastructure.Discover;
using Pulp.Tests.Infrastructure;
using Storyworthtelling.Voice;
using static Pulp.Tests.Discover.DiscoverFixtures;

namespace Pulp.Tests.Discover;

/// <summary>
/// The mood client against a fake Messages endpoint, with invented titles and an invented key: the
/// key and model tiers are read live from the shared <see cref="VoiceOptions"/>, the answer is asked
/// for as JSON and read defensively, and every failure comes back as "no idea" — never a throw.
/// </summary>
public class MoodClientTests
{
    private const string StrongTier = "strong-tier-example";
    private const string FastTier = "fast-tier-example";

    private static readonly TasteProfile SamTaste = TasteProfile.From(new Dictionary<string, int> { ["slow-burn"] = 3 });

    private static MoodQuery AdultMood() =>
        MoodQuery.For("rainy and quiet", DiscoverContext.For(MoodViewer.Adult(Sam), Roster, HouseholdServices, SamTaste));

    private static string Reply(string text) =>
        JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } }, stop_reason = "end_turn" });

    private static (MoodClient Client, BodyRecordingHandler Handler, RecordingHealthSink Health, VoiceOptions Options) Build()
    {
        var handler = new BodyRecordingHandler();
        var options = new VoiceOptions { ApiKey = "invented-mood-key", Model = StrongTier, FastModel = FastTier };
        var health = new RecordingHealthSink();
        return (new MoodClient(new HttpClient(handler), new LiveOptions(options), health), handler, health, options);
    }

    [Fact]
    public async Task AMoodAnswerIsReadIntoCandidates()
    {
        var (client, handler, health, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, Reply(
            """Here: {"title":"Rain","titles":[{"name":"Nightwater","year":2017,"kind":"film"},{"name":"Kettle & Crumb","year":"2021","kind":"show"},{"name":"Odd One"}]}"""));

        var answer = await client.AskMoodAsync(AdultMood(), CancellationToken.None);

        Assert.Equal(MoodOutcome.Answered, answer.Outcome);
        Assert.Equal(
            [new MoodCandidate("Nightwater", 2017, TitleKind.Film), new MoodCandidate("Kettle & Crumb", 2021, TitleKind.Show), new MoodCandidate("Odd One", null, null)],
            answer.Candidates);
        Assert.Equal([MoodClient.CredentialId], health.Ok);
    }

    [Fact]
    public async Task TheRequestCarriesTheKeyTheVersionTheFastTierAndOnlyWhatMayBeSent()
    {
        var (client, handler, _, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, Reply("""{"titles":[]}"""));

        await client.AskMoodAsync(AdultMood(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.Uri.ToString());
        Assert.Equal("invented-mood-key", request.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);

        using var body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal(FastTier, body.RootElement.GetProperty("model").GetString());
        Assert.True(body.RootElement.GetProperty("max_tokens").GetInt32() > 0);
        Assert.Contains("JSON", body.RootElement.GetProperty("system").GetString());
        var user = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.Contains("rainy and quiet", user);
        Assert.Contains("Exampleflix", user);
        Assert.Contains("Samplestream", user);
        Assert.Contains("slow-burn", user);
        Assert.DoesNotContain(Sam.Value, handler.Bodies.Single());
        Assert.DoesNotContain(Flix.Id.Value, handler.Bodies.Single());
    }

    [Fact]
    public async Task APlaylistRunsOnTheStrongTierAndSendsTheSeed()
    {
        var (client, handler, _, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, Reply("""{"title":"Glass","titles":[{"name":"Nightwater","year":2017}]}"""));
        var query = PlaylistQuery.For(PlaylistSeed.FromTitle(Glasshouse),
            DiscoverContext.For(MoodViewer.Adult(Sam), Roster, HouseholdServices, SamTaste));

        var answer = await client.AskPlaylistAsync(query, CancellationToken.None);

        Assert.Equal("Glass", answer.Title);
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal(StrongTier, body.RootElement.GetProperty("model").GetString());
        var user = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.Contains("The Glasshouse Year", user);
        Assert.DoesNotContain(Glasshouse.Id.Value, handler.Bodies.Single());
    }

    [Fact]
    public async Task TheKeyAndTierAreReadLiveOnEveryCall()
    {
        var (client, handler, _, options) = Build();
        handler.Enqueue(HttpStatusCode.OK, Reply("""{"titles":[]}"""));
        handler.Enqueue(HttpStatusCode.OK, Reply("""{"titles":[]}"""));

        await client.AskMoodAsync(AdultMood(), CancellationToken.None);
        options.ApiKey = "invented-replaced-key";
        options.FastModel = "fast-tier-replaced";
        await client.AskMoodAsync(AdultMood(), CancellationToken.None);

        Assert.Equal("invented-replaced-key", handler.Requests[1].Headers["x-api-key"]);
        Assert.Contains("fast-tier-replaced", handler.Bodies[1]);
    }

    [Theory]
    [InlineData("no json here at all")]
    [InlineData("{\"titles\": \"not a list\"}")]
    [InlineData("{\"title\": \"missing the list\"}")]
    [InlineData("{\"titles\": [ {\"name\": ")]
    public async Task AGarbledReplyIsNoIdeaNotACrash(string text)
    {
        var (client, handler, _, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, Reply(text));

        var answer = await client.AskMoodAsync(AdultMood(), CancellationToken.None);

        Assert.Equal(MoodOutcome.Malformed, answer.Outcome);
        Assert.Empty(answer.Candidates);
    }

    [Fact]
    public async Task ABodyThatIsNotAMessagesResponseIsNoIdea()
    {
        var (client, handler, _, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, "<html>a proxy page</html>");

        var answer = await client.AskMoodAsync(AdultMood(), CancellationToken.None);

        Assert.Equal(MoodOutcome.Malformed, answer.Outcome);
    }

    [Fact]
    public async Task CandidatesWithoutANameAreSkippedAndTheRestKept()
    {
        var (client, handler, _, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, Reply("""{"titles":[{"name":""},{"year":2001},"a string",{"name":"Nightwater","year":"soon","kind":"opera"}]}"""));

        var answer = await client.AskMoodAsync(AdultMood(), CancellationToken.None);

        Assert.Equal([new MoodCandidate("Nightwater", null, null)], answer.Candidates);
    }

    [Fact]
    public async Task ARejectedKeyIsReportedToCredentialHealthAndIsNoIdea()
    {
        var (client, handler, health, _) = Build();
        handler.Enqueue(HttpStatusCode.Unauthorized, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""");

        var answer = await client.AskMoodAsync(AdultMood(), CancellationToken.None);

        Assert.Equal(MoodOutcome.Rejected, answer.Outcome);
        Assert.Equal([MoodClient.CredentialId], health.Failing);
        Assert.Empty(health.Ok);
    }

    [Fact]
    public async Task ServerTroubleIsNoIdeaAndNotTheKeysFault()
    {
        var (client, handler, health, _) = Build();
        handler.Enqueue(HttpStatusCode.InternalServerError, """{"type":"error","error":{"type":"api_error"}}""");
        handler.Enqueue((HttpStatusCode)529, """{"type":"error","error":{"type":"overloaded_error"}}""");

        var first = await client.AskMoodAsync(AdultMood(), CancellationToken.None);
        var second = await client.AskMoodAsync(AdultMood(), CancellationToken.None);

        Assert.Equal(MoodOutcome.Unavailable, first.Outcome);
        Assert.Equal(MoodOutcome.Unavailable, second.Outcome);
        Assert.Empty(health.Failing);
    }

    [Fact]
    public async Task ANetworkFailureIsNoIdea()
    {
        var (client, handler, _, _) = Build();
        handler.Throw(new HttpRequestException("invented network drop"));

        var answer = await client.AskMoodAsync(AdultMood(), CancellationToken.None);

        Assert.Equal(MoodOutcome.Unavailable, answer.Outcome);
        Assert.Contains("invented network drop", answer.Detail);
    }

    [Fact]
    public async Task NoKeyMeansNothingIsSent()
    {
        var (client, handler, _, options) = Build();
        options.ApiKey = "  ";

        var answer = await client.AskMoodAsync(AdultMood(), CancellationToken.None);

        Assert.Equal(MoodOutcome.NoKey, answer.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ACancelledSearchStillCancels()
    {
        var (client, handler, _, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, Reply("""{"titles":[]}"""));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AskMoodAsync(AdultMood(), cancelled.Token));
    }

    /// <summary>An <see cref="IOptions{TOptions}"/> handing back the same instance, so a test can change it between calls.</summary>
    private sealed class LiveOptions(VoiceOptions options) : IOptions<VoiceOptions>
    {
        public VoiceOptions Value => options;
    }

    /// <summary>Like the catalogue clients' fake, but keeps each request's body, which the Messages API is all about.</summary>
    private sealed class BodyRecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> responses = new();

        public List<RecordedRequest> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) =>
            responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        public void Throw(Exception exception) => responses.Enqueue(() => throw exception);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var headers = request.Headers.ToDictionary(
                header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, headers));
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return responses.Dequeue()();
        }
    }
}
