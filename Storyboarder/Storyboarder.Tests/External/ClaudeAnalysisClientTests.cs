using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.Pipeline;
using Storyboarder.Infrastructure.Configuration;
using Storyboarder.Infrastructure.External;
using Storyboarder.Tests.Support;
using Xunit;

namespace Storyboarder.Tests.External;

public class ClaudeAnalysisClientTests
{
    private static (ClaudeAnalysisClient Client, FakeHttpMessageHandler Handler) Create()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new ClaudeOptions { ApiKey = "test-key", Model = "test-model", MaxTokens = 256 });
        return (new ClaudeAnalysisClient(new HttpClient(handler), options), handler);
    }

    // The Messages API streams responses as server-sent events; the client now consumes that format.
    // Reply wraps a whole assistant message as a single text_delta so the existing assertions exercise
    // the streaming path unchanged.
    private static string Reply(string assistantText) => Sse(
        ("message_start", "{\"type\":\"message_start\",\"message\":{\"role\":\"assistant\",\"content\":[]}}"),
        ("content_block_start", "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}"),
        ("content_block_delta", Delta(assistantText)),
        ("content_block_stop", "{\"type\":\"content_block_stop\",\"index\":0}"),
        ("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}"),
        ("message_stop", "{\"type\":\"message_stop\"}"));

    // A single content_block_delta event carrying the given text piece.
    private static string Delta(string textPiece) =>
        "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":" +
        JsonSerializer.Serialize(textPiece) + "}}";

    private static string Sse(params (string Event, string Data)[] events)
    {
        var builder = new StringBuilder();
        foreach (var (name, data) in events)
            builder.Append("event: ").Append(name).Append('\n').Append("data: ").Append(data).Append("\n\n");
        return builder.ToString();
    }

    [Fact]
    public async Task ExtractEntities_ParsesCharactersAndSettings()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"characters\":[{\"name\":\"Alice\",\"description\":\"tall\",\"aliases\":[\"Al\"]}]," +
            "\"settings\":[{\"name\":\"The Keep\",\"description\":\"stone tower\",\"aliases\":[]}]}"));

        var fragment = await client.ExtractEntities("excerpt", CancellationToken.None);

        var character = Assert.Single(fragment.Characters);
        Assert.Equal("Alice", character.Name);
        Assert.Equal("tall", character.Description);
        Assert.Contains("Al", character.Aliases);
        Assert.Equal("The Keep", Assert.Single(fragment.Settings).Name);
    }

    [Fact]
    public async Task ExtractEntities_SendsApiKeyVersionAndCachedSystemPrompt()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"characters\":[],\"settings\":[]}"));

        await client.ExtractEntities("excerpt", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-api-key")));
        Assert.Equal("2023-06-01", Assert.Single(request.Headers.GetValues("anthropic-version")));
        Assert.Contains("cache_control", handler.RequestBodies[0]);
        Assert.Contains("ephemeral", handler.RequestBodies[0]);
        Assert.Contains("test-model", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task ExtractEntities_ToleratesProseAroundJson()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "Sure! Here is the design bible:\n{\"characters\":[{\"name\":\"Bob\",\"description\":\"short\"}]," +
            "\"settings\":[]}\nLet me know if you need more."));

        var fragment = await client.ExtractEntities("excerpt", CancellationToken.None);

        Assert.Equal("Bob", Assert.Single(fragment.Characters).Name);
    }

    [Fact]
    public async Task Storyboard_ParsesVolumesPagesAndPanels()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"continuitySummary\":\"Alice arrived.\",\"volumes\":[{\"number\":1,\"title\":\"Beginnings\"," +
            "\"pages\":[{\"chapterNumber\":1,\"panels\":[{\"visualPrompt\":\"Alice at the gate\"," +
            "\"dialogue\":\"Hello?\",\"caption\":null,\"referenceNames\":[\"Alice\"]}]}]}]}"));

        var segment = await client.Storyboard("passage", "", new DesignBible([], []), "noir", CancellationToken.None);

        Assert.Equal("Alice arrived.", segment.ContinuitySummary);
        var volume = Assert.Single(segment.Volumes);
        Assert.Equal("Beginnings", volume.Title);
        var panel = Assert.Single(Assert.Single(volume.Pages).Panels);
        Assert.Equal("Alice at the gate", panel.VisualPrompt);
        Assert.Equal("Hello?", panel.Dialogue);
        Assert.Contains("Alice", panel.ReferenceNames);
    }

    [Fact]
    public async Task AnalyzeChapter_ParsesNotes_ToleratingProse()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "Here you go:\n{\"charactersPresent\":[\"Alice\"],\"keyEvents\":[\"Alice leaves home\"]," +
            "\"characterBeats\":[\"Alice resolves to go\"],\"setupsAndPayoffs\":[\"A locket is introduced\"]," +
            "\"possibleInconsistencies\":[\"Eye color changes\"],\"factualClaims\":[\"Paris is in France\"]," +
            "\"notableStrengths\":[\"Vivid prose\"],\"notableWeaknesses\":[\"Slow open\"]}"));

        var notes = await client.AnalyzeChapter("excerpt", ["Alice"], CancellationToken.None);

        Assert.Contains("Alice", notes.CharactersPresent);
        Assert.Equal("Eye color changes", Assert.Single(notes.PossibleInconsistencies));
        Assert.Equal("Paris is in France", Assert.Single(notes.FactualClaims));
        Assert.Equal("Slow open", Assert.Single(notes.NotableWeaknesses));
    }

    [Fact]
    public async Task AnalyzeChapter_MissingFields_DefaultToEmpty()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{}"));

        var notes = await client.AnalyzeChapter("excerpt", [], CancellationToken.None);

        Assert.Empty(notes.CharactersPresent);
        Assert.Empty(notes.NotableStrengths);
    }

    [Fact]
    public async Task ConsolidateAnalysis_ParsesReport_AndUsesAnalysisMaxTokens()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new ClaudeOptions
        {
            ApiKey = "test-key", Model = "test-model", MaxTokens = 256, AnalysisMaxTokens = 4096
        });
        var client = new ClaudeAnalysisClient(new HttpClient(handler), options);
        handler.EnqueueOk(Reply(
            "{\"logline\":\"A girl quests.\",\"synopsis\":\"She grows up.\"," +
            "\"characters\":[{\"name\":\"Alice\",\"role\":\"protagonist\",\"arcSummary\":\"grows brave\"," +
            "\"relationships\":[\"ally: Bob\"]}],\"themes\":[\"courage\"],\"plotHoles\":[\"Where is the key?\"]," +
            "\"inconsistencies\":[\"Eye color\"],\"inaccuracies\":[\"Paris claim\"],\"pacingNotes\":[\"Middle sags\"]," +
            "\"reviews\":[{\"perspective\":\"developmental editor\",\"assessment\":\"promising\"," +
            "\"strengths\":[\"voice\"],\"weaknesses\":[\"pacing\"],\"recommendation\":\"revise middle\"}]," +
            "\"revisionPlan\":[{\"priority\":\"High\",\"issue\":\"sagging middle\",\"suggestedFix\":\"cut subplot\"}]," +
            "\"comparableTitles\":[\"Book X\"],\"marketPositioning\":\"YA fantasy\"}"));

        var report = await client.ConsolidateAnalysis(
            "Quest", [ChapterNotes.Empty], new DesignBible([], []), "", CancellationToken.None);

        Assert.Equal("A girl quests.", report.Logline);
        var character = Assert.Single(report.Characters);
        Assert.Equal("Alice", character.Name);
        Assert.Equal("protagonist", character.Role);
        Assert.Equal("developmental editor", Assert.Single(report.Reviews).Perspective);
        Assert.Equal("High", Assert.Single(report.RevisionPlan).Priority);
        Assert.Equal("YA fantasy", report.MarketPositioning);

        // The reduce call must request the larger analysis output budget, not the default MaxTokens.
        Assert.Contains("\"max_tokens\":4096", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task RateLimit_429_ThrowsRateLimitException()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{\"error\":{\"type\":\"rate_limit_error\"}}");

        await Assert.ThrowsAsync<RateLimitException>(() => client.ExtractEntities("x", CancellationToken.None));
    }

    [Fact]
    public async Task RateLimit_DetectedFromErrorBody_RegardlessOfStatus()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.BadRequest, "{\"error\":{\"type\":\"overloaded_error\"}}");

        await Assert.ThrowsAsync<RateLimitException>(() => client.ExtractEntities("x", CancellationToken.None));
    }

    [Fact]
    public async Task ServerError_ThrowsHttpRequestExceptionWithStatus()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.InternalServerError, "boom");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.ExtractEntities("x", CancellationToken.None));
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public async Task SendMessage_RequestsStreaming()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"characters\":[],\"settings\":[]}"));

        await client.ExtractEntities("excerpt", CancellationToken.None);

        Assert.Contains("\"stream\":true", handler.RequestBodies[0]);
        var request = Assert.Single(handler.Requests);
        Assert.Contains(request.Headers.Accept, header => header.MediaType == "text/event-stream");
    }

    [Fact]
    public async Task SendMessage_ConcatenatesTextDeltasSplitAcrossEvents()
    {
        var (client, handler) = Create();
        // A real stream delivers the JSON in pieces across many text_delta events.
        handler.Enqueue(HttpStatusCode.OK, Sse(
            ("content_block_delta", Delta("{\"continuitySummary\":\"Ali")),
            ("content_block_delta", Delta("ce arrived.\",\"volumes\":")),
            ("content_block_delta", Delta("[]}")),
            ("message_stop", "{\"type\":\"message_stop\"}")), "text/event-stream");

        var segment = await client.Storyboard("passage", "", new DesignBible([], []), "noir", CancellationToken.None);

        Assert.Equal("Alice arrived.", segment.ContinuitySummary);
        Assert.Empty(segment.Volumes);
    }

    [Fact]
    public async Task StreamError_OverloadedEvent_ThrowsRateLimitException()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, Sse(
            ("error", "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"overloaded\"}}")),
            "text/event-stream");

        await Assert.ThrowsAsync<RateLimitException>(() => client.ExtractEntities("x", CancellationToken.None));
    }

    [Fact]
    public async Task Storyboard_CachesArtStyleAndBibleSeparatelyFromThePassage()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"continuitySummary\":\"\",\"volumes\":[]}"));

        await client.Storyboard(
            "PASSAGE: the hero walks to the gate", "prior events",
            new DesignBible([], []), "watercolour noir", CancellationToken.None);

        var body = handler.RequestBodies[0];
        // Two cached blocks: the static instruction and the per-novel art style + design bible, so
        // repeated windows/retries bill the bible as a cache read rather than full input every call.
        Assert.Equal(2, CountOccurrences(body, "cache_control"));
        Assert.Contains("watercolour noir", body);
        Assert.Contains("DESIGN BIBLE", body);
        // The varying passage rides in the uncached user message.
        Assert.Contains("the hero walks to the gate", body);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
