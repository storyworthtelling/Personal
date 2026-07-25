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
        var options = Options.Create(new ClaudeOptions
        {
            ApiKey = "test-key", Model = "test-model", FastModel = "test-fast-model", MaxTokens = 256
        });
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
    public async Task ExtractEntities_ParsesScale_AndPromptRequestsIt()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"characters\":[{\"name\":\"Alice\",\"description\":\"tall\",\"scale\":\"about 1.7m\",\"aliases\":[]}]," +
            "\"settings\":[]}"));

        var fragment = await client.ExtractEntities("excerpt", CancellationToken.None);

        Assert.Equal("about 1.7m", Assert.Single(fragment.Characters).Scale);
        // The extraction prompt must ask for scale so illustrations stay proportionate.
        Assert.Contains("scale", handler.RequestBodies[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Storyboard_PromptSteersPanelDensityAndAction()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"continuitySummary\":\"\",\"volumes\":[]}"));

        await client.Storyboard("passage", "", new DesignBible([], []), "noir", CancellationToken.None);

        var body = handler.RequestBodies[0];
        Assert.Contains("panels per page", body);
        Assert.Contains("ONGOING ACTION", body);
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
    }

    private static ChapterNotes NotesWithClaims(string chapter, params string[] claims) =>
        ChapterNotes.Empty with { Chapter = chapter, FactualClaims = claims };

    [Fact]
    public async Task CheckFacts_GrantsWebSearch_AndSendsOnlyTheClaims()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"checks\":[{\"claim\":\"Paris is the capital of Spain\",\"verdict\":\"Incorrect\"," +
            "\"note\":\"Paris is the capital of France; Madrid is Spain's.\",\"chapter\":\"Chapter 3\"}]}"));

        var checks = await client.CheckFacts(
            [NotesWithClaims("Chapter 3", "Paris is the capital of Spain")], CancellationToken.None);

        var check = Assert.Single(checks);
        Assert.Equal("Incorrect", check.Verdict);
        Assert.Equal("Chapter 3", check.Chapter);

        var body = handler.RequestBodies[0];
        // The hosted search tool is granted for this call — it is the only one that gets it.
        Assert.Contains("web_search", body);
        Assert.Contains("tools", body);
        Assert.Contains("Paris is the capital of Spain", body);
    }

    [Fact]
    public async Task OtherCalls_AreNotGivenASearchTool()
    {
        // The rest of the report must reason from the manuscript alone, not from what the web says a book
        // like this should contain.
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"continuitySummary\":\"\",\"volumes\":[]}"));

        await client.Storyboard("passage", "", new DesignBible([], []), "noir", CancellationToken.None);

        Assert.DoesNotContain("web_search", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task CheckFacts_WithNoClaims_MakesNoCallAtAll()
    {
        // Fiction that asserts nothing checkable is a normal outcome; confirming an empty list isn't worth
        // a billed request, let alone a web search.
        var (client, handler) = Create();

        var checks = await client.CheckFacts([ChapterNotes.Empty], CancellationToken.None);

        Assert.Empty(checks);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CheckFacts_MissingVerdict_DefaultsToUnverified()
    {
        // Never present an unchecked claim as settled — that would send the author after a non-problem.
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"checks\":[{\"claim\":\"The tower is 90m tall\",\"chapter\":\"Chapter 2\"}]}"));

        var checks = await client.CheckFacts(
            [NotesWithClaims("Chapter 2", "The tower is 90m tall")], CancellationToken.None);

        Assert.Equal("Unverified", Assert.Single(checks).Verdict);
    }

    [Fact]
    public async Task MechanicalPerChunkCalls_UseTheFastModel()
    {
        // Entity extraction and chapter notes run once per chunk — by far the most calls a novel makes —
        // and both are extraction against a fixed JSON contract rather than judgement, so they go to the
        // cheap tier.
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"characters\":[],\"settings\":[]}"));
        handler.EnqueueOk(Reply("{}"));

        await client.ExtractEntities("excerpt", CancellationToken.None);
        await client.AnalyzeChapter("excerpt", "Chapter 1", [], CancellationToken.None);

        Assert.Contains("\"model\":\"test-fast-model\"", handler.RequestBodies[0]);
        Assert.Contains("\"model\":\"test-fast-model\"", handler.RequestBodies[1]);
    }

    [Fact]
    public async Task JudgementCalls_StayOnTheStrongModel()
    {
        // Adapting prose into panels and synthesising the whole manuscript are the calls whose quality the
        // owner actually sees, so they must not be downgraded to save pennies.
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"continuitySummary\":\"\",\"volumes\":[]}"));
        handler.EnqueueOk(Reply("{}"));

        await client.Storyboard("passage", "", new DesignBible([], []), "noir", CancellationToken.None);
        await client.ConsolidateAnalysis("Title", [ChapterNotes.Empty], new DesignBible([], []), "", CancellationToken.None);

        Assert.Contains("\"model\":\"test-model\"", handler.RequestBodies[0]);
        Assert.Contains("\"model\":\"test-model\"", handler.RequestBodies[1]);
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
        // No shotType in the reply → Standard, so an older model reply lays out exactly as before.
        Assert.Equal(Storyboarder.Domain.Model.ShotType.Standard, panel.Shot);
    }

    [Fact]
    public async Task Storyboard_ParsesShotType_AndDefaultsUnknownToStandard()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"continuitySummary\":\"x\",\"volumes\":[{\"number\":1,\"title\":\"V\",\"pages\":[{\"chapterNumber\":1,\"panels\":[" +
            "{\"visualPrompt\":\"a\",\"dialogue\":null,\"caption\":null,\"referenceNames\":[],\"shotType\":\"splash\"}," +
            "{\"visualPrompt\":\"b\",\"dialogue\":null,\"caption\":null,\"referenceNames\":[],\"shotType\":\"WIDE\"}," +
            "{\"visualPrompt\":\"c\",\"dialogue\":null,\"caption\":null,\"referenceNames\":[],\"shotType\":\"telephoto\"}" +
            "]}]}]}"));

        var segment = await client.Storyboard("passage", "", new DesignBible([], []), "noir", CancellationToken.None);

        var panels = Assert.Single(Assert.Single(segment.Volumes).Pages).Panels;
        Assert.Equal(Storyboarder.Domain.Model.ShotType.Splash, panels[0].Shot);
        Assert.Equal(Storyboarder.Domain.Model.ShotType.Wide, panels[1].Shot);   // case-insensitive
        Assert.Equal(Storyboarder.Domain.Model.ShotType.Standard, panels[2].Shot); // unknown → Standard
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

        var notes = await client.AnalyzeChapter("excerpt", "Chapter 3", ["Alice"], CancellationToken.None);

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

        var notes = await client.AnalyzeChapter("excerpt", "Chapter 1", [], CancellationToken.None);

        Assert.Empty(notes.CharactersPresent);
        Assert.Empty(notes.NotableStrengths);
    }

    [Fact]
    public async Task AnalyzeChapter_StampsTheChapterOnTheNotes_AndSendsItAsContext()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"keyEvents\":[\"The gate opens\"]}"));

        var notes = await client.AnalyzeChapter("excerpt", "Chapters 7–8", [], CancellationToken.None);

        // The chapter is known exactly from the chunk's offsets, so it is stamped on by the client —
        // never left to the model — and it also rides along as context for the observations.
        Assert.Equal("Chapters 7–8", notes.Chapter);
        Assert.Contains("LOCATION IN THE BOOK", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task AnalyzeChapter_UnparseableReply_StillCarriesItsChapter()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("I'm afraid I can't help with that."));

        var notes = await client.AnalyzeChapter("excerpt", "Chapter 4", [], CancellationToken.None);

        Assert.Equal("Chapter 4", notes.Chapter);
        Assert.Empty(notes.KeyEvents);
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
        // A model that answered with bare strings instead of {chapter, detail} still parses; the
        // finding simply has nothing to cite.
        Assert.Equal("Where is the key?", Assert.Single(report.PlotHoles).Detail);
        Assert.Equal(string.Empty, Assert.Single(report.PlotHoles).Chapter);

        // The reduce call must request the larger analysis output budget, not the default MaxTokens.
        Assert.Contains("\"max_tokens\":4096", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task ConsolidateAnalysis_CarriesChapterCitationsOntoFindingsAndThePlan()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"logline\":\"A girl quests.\",\"plotHoles\":[{\"chapter\":\"Chapter 4\",\"detail\":\"The key is never found.\"}]," +
            "\"pacingNotes\":[{\"chapter\":\"Chapters 7–8\",\"detail\":\"The middle sags.\"}]," +
            "\"revisionPlan\":[{\"priority\":\"High\",\"chapter\":\"Chapter 4\",\"issue\":\"Sagging middle\"," +
            "\"suggestedFix\":\"Cut a subplot.\"}]}"));

        var report = await client.ConsolidateAnalysis(
            "Quest", [ChapterNotes.Empty], new DesignBible([], []), "", CancellationToken.None);

        Assert.Equal("Chapter 4", Assert.Single(report.PlotHoles).Chapter);
        Assert.Equal("The key is never found.", Assert.Single(report.PlotHoles).Detail);
        Assert.Equal("Chapters 7–8", Assert.Single(report.PacingNotes).Chapter);
        Assert.Equal("Chapter 4", Assert.Single(report.RevisionPlan).Chapter);
        // The prompt has to ask for the citation, or the model has no reason to carry it through.
        Assert.Contains("chapter", handler.RequestBodies[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConsolidateAnalysis_LeavesTheCraftSectionsToTheirOwnCall()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"logline\":\"A girl quests.\"}"));

        var report = await client.ConsolidateAnalysis(
            "Quest", [ChapterNotes.Empty], new DesignBible([], []), "", CancellationToken.None);

        Assert.Empty(report.LineEdits);
        Assert.Empty(report.ProseNotes);
        Assert.Empty(report.DialogueNotes);
        Assert.Empty(report.SceneBreakdown);
    }

    [Fact]
    public async Task ConsolidateCraft_ParsesAllFourSections_AndUsesTheCraftBudget()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new ClaudeOptions
        {
            ApiKey = "test-key", Model = "test-model", MaxTokens = 256, CraftMaxTokens = 9001
        });
        var client = new ClaudeAnalysisClient(new HttpClient(handler), options);
        handler.EnqueueOk(Reply(
            "{\"lineEdits\":[{\"chapter\":\"Chapter 2\",\"excerpt\":\"He felt the cold.\"," +
            "\"suggestion\":\"The cold came.\",\"reason\":\"Filtered.\"}]," +
            "\"proseNotes\":[{\"chapter\":\"Chapter 3\",\"pattern\":\"Filter words\"," +
            "\"evidence\":\"she felt\",\"fix\":\"Cut the filter.\"}]," +
            "\"dialogueNotes\":[{\"chapter\":\"Chapter 4\",\"speakers\":\"Alice, Bob\"," +
            "\"observation\":\"They sound identical.\",\"fix\":\"Give Bob a shorter line.\"}]," +
            "\"sceneBreakdown\":[{\"chapter\":\"Chapter 5\",\"scene\":\"Alice waits.\"," +
            "\"purpose\":\"Stalls the confrontation.\",\"verdict\":\"Does no work.\"}]}"));

        var craft = await client.ConsolidateCraft("Quest", [ChapterNotes.Empty], CancellationToken.None);

        Assert.Equal("Chapter 2", Assert.Single(craft.LineEdits).Chapter);
        Assert.Equal("The cold came.", Assert.Single(craft.LineEdits).Suggestion);
        Assert.Equal("Filter words", Assert.Single(craft.ProseNotes).Pattern);
        Assert.Equal("Alice, Bob", Assert.Single(craft.DialogueNotes).Speakers);
        Assert.Equal("Does no work.", Assert.Single(craft.SceneBreakdown).Verdict);

        // The craft half is the larger of the two reduce calls and must request its own budget.
        Assert.Contains("\"max_tokens\":9001", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task ConsolidateCraft_MissingOrUnparseableSections_DegradeToEmpty()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"lineEdits\":[{\"chapter\":\"Chapter 2\"}]}"));

        var craft = await client.ConsolidateCraft("Quest", [ChapterNotes.Empty], CancellationToken.None);

        var edit = Assert.Single(craft.LineEdits);
        Assert.Equal("Chapter 2", edit.Chapter);
        Assert.Equal(string.Empty, edit.Excerpt);
        Assert.Empty(craft.SceneBreakdown);
    }

    [Fact]
    public async Task AnalyzeChapter_GathersTheCraftObservations_WhileTheSentencesAreStillAvailable()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"lineEditCandidates\":[\"\\\"He felt the cold.\\\" — cut the filter\"]," +
            "\"proseObservations\":[\"Filter words recur\"],\"dialogueObservations\":[\"Alice and Bob sound alike\"]," +
            "\"sceneBeats\":[\"Alice waits | stalls the confrontation | does no work\"]}"));

        var notes = await client.AnalyzeChapter("excerpt", "Chapter 2", [], CancellationToken.None);

        Assert.Single(notes.LineEditCandidates);
        Assert.Single(notes.ProseObservations);
        Assert.Single(notes.DialogueObservations);
        Assert.Single(notes.SceneBeats);
        // By consolidation time only the notes survive, so the prompt must gather these per chunk.
        Assert.Contains("lineEditCandidates", handler.RequestBodies[0]);
        Assert.Contains("sceneBeats", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task Storyboard_UsesStoryboardMaxTokens_NotDefault()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new ClaudeOptions
        {
            ApiKey = "test-key", Model = "test-model", MaxTokens = 256, StoryboardMaxTokens = 32000
        });
        var client = new ClaudeAnalysisClient(new HttpClient(handler), options);
        handler.EnqueueOk(Reply("{\"continuitySummary\":\"\",\"volumes\":[]}"));

        await client.Storyboard("passage", "", new DesignBible([], []), "noir", CancellationToken.None);

        // A full panel breakdown of a chunk is the heaviest output the pipeline produces, so the
        // storyboard call must request its own large budget rather than the small default MaxTokens.
        Assert.Contains("\"max_tokens\":32000", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task MaxTokensStopReason_ThrowsTruncatedResponse_NotSilentPartialBody()
    {
        var (client, handler) = Create();
        // The model hit its output ceiling mid-response: partial (unterminated) JSON plus a
        // message_delta reporting stop_reason "max_tokens". Returning this silently would fail
        // downstream with an opaque JSON error (e.g. "ZeroDepthAtEnd"); the client must surface it.
        handler.Enqueue(HttpStatusCode.OK, Sse(
            ("content_block_delta", Delta("{\"characters\":[{\"name\":\"Ali")),
            ("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\"}}"),
            ("message_stop", "{\"type\":\"message_stop\"}")), "text/event-stream");

        var ex = await Assert.ThrowsAsync<ResponseTruncatedException>(
            () => client.ExtractEntities("x", CancellationToken.None));
        Assert.Contains("max_tokens", ex.Message);
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
    public async Task RewritePromptToImply_ReturnsRestagedPrompt()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("The lovers embrace; the camera drifts to the rain-streaked window."));

        var reworked = await client.RewritePromptToImply(
            "an explicit love scene", CancellationToken.None);

        Assert.Equal("The lovers embrace; the camera drifts to the rain-streaked window.", reworked);
        // The restage instruction is sent as the (cached) system prompt.
        Assert.Contains("IMPLIED", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task RewritePromptToImply_FallsBackToOriginal_WhenModelReturnsNothing()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("   "));

        var reworked = await client.RewritePromptToImply("original prompt", CancellationToken.None);

        Assert.Equal("original prompt", reworked);
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
