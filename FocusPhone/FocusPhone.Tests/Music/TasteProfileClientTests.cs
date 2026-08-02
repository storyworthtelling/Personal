using System.Text.Json;
using Microsoft.Extensions.Options;
using FocusPhone.Domain.Music;
using FocusPhone.Infrastructure.Music;
using FocusPhone.Tests.Support;
using Storyworthtelling.Voice;
using Xunit;

namespace FocusPhone.Tests.Music;

public class TasteProfileClientTests
{
    private static readonly VoiceOptions Options = new()
    {
        ApiKey = "sk-test",
        Model = "claude-sonnet-4-6",
        FastModel = "claude-haiku-4-5-20251001"
    };

    private static string MessageResponse(string text) =>
        JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } }, stop_reason = "end_turn" });

    private static TasteProfileClient Client(QueuedHttpMessageHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options));

    private static TasteEvidence Evidence(int sessions = 9) =>
        new(
            [new AffinityStat("Jazz", sessions, 20, 2, 3, 15, 5)],
            [new AffinityStat("Bill Evans", sessions, 18, 1, 3, 14, 4)],
            [new AffinityStat("Bill Evans", sessions, 18, 1, 3, 14, 4)],
            new int[24],
            sessions);

    [Fact]
    public async Task RefineAsync_BuildsProfileFromReply_OnStrongModelWithKey()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """{"summary":"Leans jazz, album-first, late at night.","loves":["bebop piano"],"leans":["albums over shuffle"],"avoids":["hype"],"topGenres":["Jazz"],"topArtists":["Bill Evans"],"listensBy":["nights"],"explicitAdvisory":false}"""));

        var profile = await Client(handler).RefineAsync(TasteProfile.Empty, Evidence(sessions: 9), CancellationToken.None);

        Assert.Equal("Leans jazz, album-first, late at night.", profile.Summary);
        Assert.Equal(["Jazz"], profile.TopGenres);
        Assert.Equal(["Bill Evans"], profile.TopArtists);
        Assert.Equal(9, profile.SessionsAnalyzed); // provenance from evidence, not the model

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("sk-test", request.Headers.GetValues("x-api-key").Single());

        var body = handler.RequestBodies.Single();
        Assert.Contains("claude-sonnet-4-6", body); // strong tier for the analysis
        Assert.Contains("Jazz", body); // evidence digest reached the model
    }

    [Fact]
    public async Task RefineAsync_ToleratesJsonWrappedInProse()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse(
            """Sure! {"summary":"Plain.","loves":[],"leans":[],"avoids":[],"topGenres":[],"topArtists":[],"listensBy":[],"explicitAdvisory":true} done."""));

        var profile = await Client(handler).RefineAsync(TasteProfile.Empty, Evidence(), CancellationToken.None);

        Assert.Equal("Plain.", profile.Summary);
        Assert.True(profile.ExplicitAdvisory);
    }

    [Fact]
    public async Task RefineAsync_GarbledReply_ReturnsExistingUnchanged()
    {
        var handler = new QueuedHttpMessageHandler().EnqueueOk(MessageResponse("no json here"));
        var existing = new TasteProfile("Existing.", ["a"], [], [], ["Rock"], [], [], false, 4);

        var profile = await Client(handler).RefineAsync(existing, Evidence(), CancellationToken.None);

        Assert.Same(existing, profile);
    }

    [Fact]
    public async Task RefineAsync_HttpError_Throws()
    {
        var handler = new QueuedHttpMessageHandler().Enqueue(System.Net.HttpStatusCode.Unauthorized, "bad key");
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(handler).RefineAsync(TasteProfile.Empty, Evidence(), CancellationToken.None));
    }
}
