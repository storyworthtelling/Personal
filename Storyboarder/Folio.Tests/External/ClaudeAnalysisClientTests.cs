using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Folio.Domain.Analysis;
using Folio.Domain.Model;
using Folio.Domain.Reading;
using Folio.Domain.Scenes;
using Folio.Domain.Text;
using Folio.Domain.Pipeline;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.External;
using Folio.Tests.Support;
using Xunit;
using Storyworthtelling.Pipeline;

namespace Folio.Tests.External;

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
    public async Task ExtractEntities_PromptDemandsExhaustiveCoverage()
    {
        // The whole point of extraction is reference-sheet coverage: every character (including minor and
        // background) and every setting/set the excerpt describes must be listed, or the illustrator
        // invents them and consistency breaks. The prompt must insist on exhaustiveness, not just "every
        // distinct" (which the model reads as "the notable ones").
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"characters\":[],\"settings\":[]}"));

        await client.ExtractEntities("excerpt", CancellationToken.None);

        var body = handler.RequestBodies[0];
        Assert.Contains("EXHAUSTIVE", body);
        Assert.Contains("minor", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background", body, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task EntityExtraction_UsesTheStrongModel_ButChapterNotesStayCheap()
    {
        // Entity extraction drives how many reference sheets get drawn, and the cheap model under-extracts
        // — it drops minor characters and described sets, which the illustrator then has no reference for
        // (wrong faces, drifting locations). So extraction runs on the strong model. Chapter notes are
        // genuinely mechanical note-taking against a fixed contract, so they stay on the cheap tier.
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"characters\":[],\"settings\":[]}"));
        handler.EnqueueOk(Reply("{}"));

        await client.ExtractEntities("excerpt", CancellationToken.None);
        await client.AnalyzeChapter("excerpt", "Chapter 1", [], string.Empty, CancellationToken.None);

        Assert.Contains("\"model\":\"test-model\"", handler.RequestBodies[0]);
        Assert.Contains("\"model\":\"test-fast-model\"", handler.RequestBodies[1]);
    }

    [Fact]
    public async Task JudgementCalls_StayOnTheStrongModel()
    {
        // Synthesising the whole manuscript into the report is a call whose quality the owner actually
        // sees, so it must not be downgraded to save pennies.
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{}"));

        await client.ConsolidateAnalysis("Title", [ChapterNotes.Empty], new DesignBible([], []), "", CancellationToken.None);

        Assert.Contains("\"model\":\"test-model\"", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task EveryCall_SetsItsEffortExplicitly_AndNoneOfThemDefaultsToHigh()
    {
        // Effort is set on every request rather than inherited from a default that changes with the model.
        // At high, the model's reasoning shares the output limit with the reply: the long structured
        // replies were cut off (the reasoning took most of the 16,000-20,000 tokens) and, on a real book,
        // the first whole-book read spent all 8,000 of its tokens thinking and wrote nothing, twelve times.
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"characters\":[],\"settings\":[]}"));
        handler.EnqueueOk(Reply("{}"));
        handler.EnqueueOk(Reply("{}"));
        handler.EnqueueOk(Reply("{\"summary\":\"\",\"findings\":[]}"));

        await client.ExtractEntities("excerpt", CancellationToken.None);
        await client.AnalyzeChapter("excerpt", "Chapter 1", [], string.Empty, CancellationToken.None);
        await client.ConsolidateAnalysis("Title", [ChapterNotes.Empty], new DesignBible([], []), "", CancellationToken.None);
        await client.ReadSection(PersonaCatalogue.Sections[0], "the manuscript", GenreFrame.Unknown, CancellationToken.None);

        Assert.Equal(4, handler.RequestBodies.Count);
        foreach (var body in handler.RequestBodies)
            Assert.Contains("\"output_config\":{\"effort\":\"medium\"}", body);
    }

    /// <summary>
    /// A reader's request carries the manuscript, its own instruction (word count and genre among it)
    /// and its own question — and nothing else: one user message, no history, no other pass's notes.
    /// "None of them saw the others' notes" is printed on the report, so it is held here against the
    /// request actually sent rather than against the wording of a prompt.
    /// </summary>
    [Fact]
    public async Task AReadersRequestCarriesNothingButTheManuscriptAndItsOwnBrief()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"verdict\":\"v\",\"assessment\":\"a\",\"strengths\":[\"s\"],"
            + "\"weaknesses\":[\"w\"],\"condition\":\"c\",\"score\":7,\"scoreReason\":\"r\"}"));
        var persona = PersonaCatalogue.ById("genre-fan");
        const string manuscript = "The tide was out and the wall was down.";
        var genre = new GenreFrame("Cozy mystery", "70,000-90,000");

        await client.ReadAsReader(persona, manuscript, genre, CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var system = body.RootElement.GetProperty("system").EnumerateArray()
            .Select(block => block.GetProperty("text").GetString()).ToList();
        var messages = body.RootElement.GetProperty("messages").EnumerateArray().ToList();

        Assert.Equal(ReadingPrompt.System(manuscript, persona, genre).Select(block => block.Text), system);
        var message = Assert.Single(messages);
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.StartsWith(ReadingPrompt.Question(persona, StoryLength.ShortStory),
            message.GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Contains("Cozy mystery", system[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("7", 7)]
    [InlineData("\"8\"", 8)]
    [InlineData("6.6", 7)]
    public async Task AReadersScoreIsReadAsAWholeNumber(string score, int expected)
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"verdict\":\"v\",\"assessment\":\"a\",\"strengths\":[\"s\"],"
            + "\"weaknesses\":[\"w\"],\"condition\":\"c\",\"score\":" + score + ",\"scoreReason\":\"Because.\"}"));

        var review = await client.ReadAsReader(PersonaCatalogue.ById("agent"), "the manuscript", GenreFrame.Unknown, CancellationToken.None);

        Assert.Equal(expected, review.Score);
        Assert.Equal("Because.", review.ScoreReason);
    }

    /// <summary>A read with no score is a short read, retried like any other.</summary>
    [Theory]
    [InlineData(",\"scoreReason\":\"r\"")]
    [InlineData(",\"score\":0,\"scoreReason\":\"r\"")]
    [InlineData(",\"score\":12,\"scoreReason\":\"r\"")]
    [InlineData(",\"score\":7")]
    public async Task AReadWithoutAUsableScoreIsAFailedPass(string tail)
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"verdict\":\"v\",\"assessment\":\"a\",\"strengths\":[\"s\"],"
            + "\"weaknesses\":[\"w\"],\"condition\":\"c\"" + tail + "}"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ReadAsReader(PersonaCatalogue.ById("agent"), "the manuscript", GenreFrame.Unknown, CancellationToken.None));
    }

    /// <summary>
    /// A scene is judged from its own text, the story so far and the book's voice, and the reply comes
    /// back as one scene's judgement (release A3–A4).
    /// </summary>
    [Fact]
    public async Task JudgeScene_SendsTheSceneAndItsContext_AndReadsTheJudgement()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"accomplishes\":\"Sets the grief.\",\"call\":\"Tighten\",\"tension\":0.7,"
            + "\"verdict\":\"Repeats itself.\",\"inconsistencies\":[{\"kind\":\"Tense\",\"detail\":\"Slips.\","
            + "\"excerpt\":\"nobody spoke\"}]}"));

        var judgement = await client.JudgeScene(
            new SceneToJudge(4, "Chapter 2", "She walked the sea wall.", "Her brother drowned.", "Measured, past tense."),
            new DesignBible([], []), CancellationToken.None);

        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("She walked the sea wall.", body, StringComparison.Ordinal);
        Assert.Contains("Her brother drowned.", body, StringComparison.Ordinal);
        Assert.Contains("Measured, past tense.", body, StringComparison.Ordinal);
        Assert.Equal(4, judgement.SceneNumber);
        Assert.Equal("Chapter 2", judgement.Chapter);
        Assert.Equal(SceneCall.Tighten, judgement.Call);
        Assert.Equal(0.7, judgement.Tension, 3);
        var inconsistency = Assert.Single(judgement.Inconsistencies);
        Assert.Equal(SceneInconsistencyKind.Tense, inconsistency.Kind);
        Assert.Equal("nobody spoke", inconsistency.Excerpt);
    }

    /// <summary>
    /// Every call that produces findings asks for each one's severity — Must, Should or Consider — and
    /// the answer reaches the finding (R8), so a mark's colour is the model's judgement of that note
    /// rather than the revision plan's verdict on the chapter it happens to sit in.
    /// </summary>
    [Fact]
    public async Task EveryCallThatWritesFindingsAsksForEachOnesSeverityAndKeepsIt()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"plotHoles\":[{\"chapter\":\"Chapter 1\",\"detail\":\"d\",\"excerpt\":\"\",\"severity\":\"Must\"}]}"));
        handler.EnqueueOk(Reply("{\"lineEdits\":[{\"chapter\":\"Chapter 1\",\"excerpt\":\"e\",\"suggestion\":\"s\",\"reason\":\"r\",\"severity\":\"Must\"}],"
            + "\"proseNotes\":[{\"chapter\":\"Chapter 1\",\"pattern\":\"p\",\"evidence\":\"e\",\"fix\":\"f\",\"severity\":\"Must\"}],"
            + "\"dialogueNotes\":[{\"chapter\":\"Chapter 1\",\"speakers\":\"s\",\"observation\":\"o\",\"fix\":\"f\",\"severity\":\"Must\"}]}"));
        handler.EnqueueOk(Reply("{\"dominantVoice\":\"v\",\"divergences\":[{\"chapter\":\"Chapter 1\",\"excerpt\":\"e\",\"divergence\":\"d\",\"suggestion\":\"s\",\"severity\":\"Must\"}]}"));
        handler.EnqueueOk(Reply("{\"scenesToAdd\":[],\"showVsTell\":[{\"chapter\":\"Chapter 1\",\"excerpt\":\"e\",\"direction\":\"Tighten\",\"suggestion\":\"s\",\"severity\":\"Must\"}],\"targetLow\":0,\"targetHigh\":0}"));
        handler.EnqueueOk(Reply("{\"breaks\":[{\"chapter\":\"Chapter 1\",\"established\":\"e\",\"contradiction\":\"c\",\"excerpt\":\"\",\"severity\":\"Must\"}],\"carriedThreads\":[]}"));
        handler.EnqueueOk(Reply("{\"summary\":\"s\",\"findings\":[{\"chapter\":\"Chapter 1\",\"detail\":\"d\",\"excerpt\":\"\",\"severity\":\"Must\"}]}"));
        handler.EnqueueOk(Reply("{\"accomplishes\":\"a\",\"call\":\"Keep\",\"tension\":0.5,\"verdict\":\"v\","
            + "\"inconsistencies\":[{\"kind\":\"Tense\",\"detail\":\"d\",\"excerpt\":\"e\",\"severity\":\"Must\"}]}"));

        var analysis = await client.ConsolidateAnalysis("Title", [ChapterNotes.Empty], new DesignBible([], []), "", CancellationToken.None);
        var craft = await client.ConsolidateCraft("Title", [ChapterNotes.Empty], CancellationToken.None);
        var voice = await client.AssessVoiceConsistency([new ChapterSample("Chapter 1", "a"), new ChapterSample("Chapter 2", "b")], CancellationToken.None);
        var expansion = await client.ProposeExpansion("Title", [ChapterNotes.Empty], "", 1000, "Mystery", CancellationToken.None);
        var series = await client.AssessSeriesContinuity("Title", new SeriesCanon("Book One", [], [], "", []), "", null, [ChapterNotes.Empty], "", CancellationToken.None);
        var section = await client.ReadSection(PersonaCatalogue.Sections[0], "the manuscript", GenreFrame.Unknown, CancellationToken.None);
        var scene = await client.JudgeScene(new SceneToJudge(1, "Chapter 1", "t", "", ""), new DesignBible([], []), CancellationToken.None);

        Assert.Equal(7, handler.RequestBodies.Count);
        foreach (var body in handler.RequestBodies)
        {
            Assert.Contains("severity", body, StringComparison.Ordinal);
            Assert.Contains("Must", body, StringComparison.Ordinal);
            Assert.Contains("Should", body, StringComparison.Ordinal);
            Assert.Contains("Consider", body, StringComparison.Ordinal);
        }

        string[] severities =
        [
            Assert.Single(analysis.PlotHoles).Severity,
            Assert.Single(craft.LineEdits).Severity,
            Assert.Single(craft.ProseNotes).Severity,
            Assert.Single(craft.DialogueNotes).Severity,
            Assert.Single(voice.Divergences).Severity,
            Assert.Single(expansion.ShowVsTell).Severity,
            Assert.Single(series.Breaks).Severity,
            Assert.Single(section.Findings).Severity,
            Assert.Single(scene.Inconsistencies).Severity,
        ];
        Assert.All(severities, severity => Assert.Equal("Must", severity));
    }

    /// <summary>
    /// The plan's synthesis is shown every keyed finding and says, per item, which it draws on (R15) — so
    /// the report can link the item to the place. A key the model made up is dropped, not followed.
    /// </summary>
    [Fact]
    public async Task ThePlanIsShownTheKeyedFindingsAndNamesTheOnesEachItemDrawsOn()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"revisionPlan\":[{\"priority\":\"Must\",\"chapter\":\"Ch. 2\",\"issue\":\"i\","
            + "\"suggestedFix\":\"s\",\"draws\":[\"f0000beef\",\"fdeadbeef\"]}]}"));
        IReadOnlyList<Folio.Domain.Markup.FindingEntry> findings =
            [new("f0000beef", "Plot holes", "Plot hole", "Chapter 2", "Nobody reacts.", Folio.Domain.Readiness.ReadinessTier.Must)];

        var read = await client.Synthesise("todo", [], [], 90_000, findings, [], CancellationToken.None);

        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("f0000beef", body, StringComparison.Ordinal);
        Assert.Contains("Nobody reacts.", body, StringComparison.Ordinal);
        Assert.Contains("draws", body, StringComparison.Ordinal);
        Assert.Equal(["f0000beef"], Assert.Single(read.RevisionPlan).Draws);
    }

    /// <summary>Each chapter on the pacing curve says in a few words what drives or drains it (R17).</summary>
    [Fact]
    public async Task ThePacingCurveCarriesWhatDrivesEachChapter()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"pacingCurve\":[{\"chapter\":\"Chapter 1\",\"tension\":0.4,\"driver\":\"The storm builds\"}]}"));

        var analysis = await client.ConsolidateAnalysis("Title", [ChapterNotes.Empty], new DesignBible([], []), "", CancellationToken.None);

        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("driver", body, StringComparison.Ordinal);
        Assert.Equal("The storm builds", Assert.Single(analysis.PacingCurve).Driver);
    }

    /// <summary>A section is asked for one short paragraph: four sentences for a book, two for a short story (R16).</summary>
    [Theory]
    [InlineData(500, "at most two sentences")]
    [InlineData(90_000, "at most four sentences")]
    public async Task ASectionIsAskedForAParagraphItsLengthAllows(int words, string asked)
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"summary\":\"s\",\"findings\":[]}"));
        var manuscript = string.Join(' ', Enumerable.Repeat("word", words));

        await client.ReadSection(PersonaCatalogue.Sections[0], manuscript, GenreFrame.Unknown, CancellationToken.None);

        Assert.Contains(asked, Assert.Single(handler.RequestBodies), StringComparison.Ordinal);
    }

    /// <summary>
    /// On a later draft the plan pass is shown the last plan, says where each new item stands against it,
    /// and names the last plan's items that are now fixed (R19). A number it was not shown is dropped.
    /// </summary>
    [Fact]
    public async Task ThePlanIsComparedWithTheLastDraftsPlan()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"revisionPlan\":[{\"priority\":\"Must\",\"chapter\":\"Ch. 2\",\"issue\":\"The middle sags\","
            + "\"suggestedFix\":\"Cut a subplot.\",\"since\":\"open\"}],\"fixed\":[2, 9]}"));
        IReadOnlyList<RevisionItem> previous =
        [
            new("Must", "Ch. 2", "The middle sags", "Cut a subplot."),
            new("Should", "Ch. 5", "The ending is rushed", "Give the last scene room."),
        ];

        var read = await client.Synthesise("todo", [], [], 90_000, [], previous, CancellationToken.None);

        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("The ending is rushed", body, StringComparison.Ordinal);
        Assert.Contains("fixed", body, StringComparison.Ordinal);
        Assert.Equal("open", Assert.Single(read.RevisionPlan).Since);
        Assert.Equal("The ending is rushed", Assert.Single(read.Fixed).Issue);
    }

    /// <summary>A first draft has no last plan, so the pass is not asked to compare.</summary>
    [Fact]
    public async Task AFirstDraftsPlanIsNotAskedToCompare()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"revisionPlan\":[]}"));

        await client.Synthesise("todo", [], [], 90_000, [], [], CancellationToken.None);

        Assert.DoesNotContain("previousPlan", Assert.Single(handler.RequestBodies), StringComparison.Ordinal);
    }

    /// <summary>A judgement with no call or nothing accomplished is a failed pass, retried, never printed half-made.</summary>
    [Theory]
    [InlineData("{\"accomplishes\":\"x\",\"call\":\"Maybe\",\"tension\":0.5,\"verdict\":\"v\"}")]
    [InlineData("{\"accomplishes\":\"\",\"call\":\"Keep\",\"tension\":0.5,\"verdict\":\"v\"}")]
    [InlineData("not json")]
    public async Task JudgeScene_RefusesAnIncompleteJudgement(string reply)
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(reply));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.JudgeScene(
            new SceneToJudge(1, "Chapter 1", "Text.", "", ""), new DesignBible([], []), CancellationToken.None));
    }

    /// <summary>Nudge never write: the instruction forbids replacement prose in the verdict and the notes.</summary>
    [Fact]
    public async Task JudgeScene_IsToldNeverToWriteTheAuthorsSentences()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"accomplishes\":\"a\",\"call\":\"Keep\",\"tension\":0.2,\"verdict\":\"v\",\"inconsistencies\":[]}"));

        await client.JudgeScene(new SceneToJudge(1, "Chapter 1", "Text.", "", ""), new DesignBible([], []), CancellationToken.None);

        Assert.Contains("never write replacement prose", Assert.Single(handler.RequestBodies), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The scene judgements go to the batch tier as one request per scene, keyed by the scene's own
    /// number, built exactly as the direct call is (same instruction, same content), and never streamed.
    /// </summary>
    [Fact]
    public async Task SubmitSceneJudgements_SendsOneRequestPerSceneKeyedByItsNumber()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"id\":\"msgbatch_1\",\"processing_status\":\"in_progress\"}");

        var id = await client.SubmitSceneJudgements(
            [new SceneToJudge(3, "Chapter 1", "The tide went out.", "", ""), new SceneToJudge(7, "Chapter 2", "The bell rang.", "Before.", "")],
            new DesignBible([], []), CancellationToken.None);

        Assert.Equal("msgbatch_1", id);
        Assert.Equal("https://api.anthropic.com/v1/messages/batches", Assert.Single(handler.Requests).RequestUri!.ToString());
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var requests = body.RootElement.GetProperty("requests").EnumerateArray().ToList();
        Assert.Equal(["scene-3", "scene-7"], requests.Select(r => r.GetProperty("custom_id").GetString()));
        var first = requests[0].GetProperty("params");
        Assert.False(first.TryGetProperty("stream", out _));
        Assert.Contains("The tide went out.", first.GetProperty("messages")[0].GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Contains("never write replacement prose", first.GetProperty("system")[0].GetProperty("text").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BatchEnded_ReadsTheProcessingStatus()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"id\":\"msgbatch_1\",\"processing_status\":\"in_progress\"}");
        handler.EnqueueOk("{\"id\":\"msgbatch_1\",\"processing_status\":\"ended\",\"results_url\":\"https://api.anthropic.com/v1/messages/batches/msgbatch_1/results\"}");

        Assert.False(await client.BatchEnded("msgbatch_1", CancellationToken.None));
        Assert.True(await client.BatchEnded("msgbatch_1", CancellationToken.None));
        Assert.Equal("https://api.anthropic.com/v1/messages/batches/msgbatch_1", handler.Requests[0].RequestUri!.ToString());
    }

    /// <summary>
    /// Results come back in any order and are matched by custom id, never by position. A failed or
    /// unreadable item is reported as a failure for that scene, not dropped and not shifted.
    /// </summary>
    [Fact]
    public async Task SceneJudgementResults_AreMatchedByCustomIdAndFailuresAreKept()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"id\":\"msgbatch_1\",\"processing_status\":\"ended\",\"results_url\":\"https://api.anthropic.com/v1/messages/batches/msgbatch_1/results\"}");
        var judged = "{\\\"accomplishes\\\":\\\"Sets the grief.\\\",\\\"call\\\":\\\"Cut\\\",\\\"tension\\\":0.1,\\\"verdict\\\":\\\"v\\\",\\\"inconsistencies\\\":[]}";
        handler.Enqueue(System.Net.HttpStatusCode.OK,
            "{\"custom_id\":\"scene-7\",\"result\":{\"type\":\"succeeded\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"" + judged + "\"}],\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}}}\n" +
            "{\"custom_id\":\"scene-3\",\"result\":{\"type\":\"errored\",\"error\":{\"type\":\"api_error\",\"message\":\"overloaded\"}}}\n",
            "application/binary");

        var results = await client.SceneJudgementResults(
            "msgbatch_1", new Dictionary<int, string> { [3] = "Chapter 1", [7] = "Chapter 2" }, CancellationToken.None);

        var seven = Assert.Single(results, r => r.SceneNumber == 7);
        Assert.Equal(SceneCall.Cut, seven.Value!.Call);
        Assert.Equal("Chapter 2", seven.Value.Chapter);
        var three = Assert.Single(results, r => r.SceneNumber == 3);
        Assert.Null(three.Value);
        Assert.Contains("overloaded", three.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitSceneImagePrompts_SendsOneRequestPerScene()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"id\":\"msgbatch_2\",\"processing_status\":\"in_progress\"}");

        var id = await client.SubmitSceneImagePrompts(
            [new SceneImageRequest(1, "A lighthouse at dusk."), new SceneImageRequest(2, "The bell in the fog.")],
            new DesignBible([], []), "linocut", CancellationToken.None);

        Assert.Equal("msgbatch_2", id);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var requests = body.RootElement.GetProperty("requests").EnumerateArray().ToList();
        Assert.Equal(["scene-1", "scene-2"], requests.Select(r => r.GetProperty("custom_id").GetString()));
        Assert.Contains("ART STYLE: linocut", requests[0].GetProperty("params").GetProperty("messages")[0].GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A reader writes more at novel length (R12), so its pass gets more room; a short story's or a
    /// section's pass keeps the ordinary ceiling.
    /// </summary>
    [Fact]
    public async Task AReaderOnANovelGetsTheNovelCeiling()
    {
        var (client, handler) = Create();
        var whole = "{\"verdict\":\"v\",\"assessment\":\"a\",\"strengths\":[\"s\"],\"weaknesses\":[\"w\"],\"condition\":\"c\",\"score\":7,\"scoreReason\":\"r\"}";
        handler.EnqueueOk(Reply(whole));
        handler.EnqueueOk(Reply(whole));
        handler.EnqueueOk(Reply("{\"summary\":\"\",\"findings\":[]}"));
        var novel = string.Join(' ', Enumerable.Repeat("word", 90_000));
        var options = new ClaudeOptions();

        await client.ReadAsReader(PersonaCatalogue.ById("agent"), novel, GenreFrame.Unknown, CancellationToken.None);
        await client.ReadAsReader(PersonaCatalogue.ById("agent"), "A short story.", GenreFrame.Unknown, CancellationToken.None);
        await client.ReadSection(PersonaCatalogue.Sections[0], novel, GenreFrame.Unknown, CancellationToken.None);

        int MaxTokens(int i) => JsonDocument.Parse(handler.RequestBodies[i]).RootElement.GetProperty("max_tokens").GetInt32();
        Assert.Equal(options.NovelReaderMaxTokens, MaxTokens(0));
        Assert.Equal(options.PassMaxTokens, MaxTokens(1));
        Assert.Equal(options.PassMaxTokens, MaxTokens(2));
        Assert.True(options.NovelReaderMaxTokens > options.PassMaxTokens);
    }

    /// <summary>
    /// Batched reads (R39) are built exactly as the direct passes are: the same system blocks (manuscript
    /// first, cached), the same question and format, the model for the persona's tier, the pass effort,
    /// and the novel ceiling for a reader on a novel. Keyed by persona id.
    /// </summary>
    [Fact]
    public async Task SubmitReads_BuildsEachPassAsTheDirectCallWould()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"id\":\"msgbatch_reads\",\"processing_status\":\"in_progress\"}");
        const string manuscript = "The tide was out and the wall was down.";
        var personas = new[] { PersonaCatalogue.ById("agent"), PersonaCatalogue.ById("genre-fan") };

        var id = await client.SubmitReads(personas, manuscript, GenreFrame.Unknown, CancellationToken.None);

        Assert.Equal("msgbatch_reads", id);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var requests = body.RootElement.GetProperty("requests").EnumerateArray().ToList();
        Assert.Equal(["pass-agent", "pass-genre-fan"], requests.Select(r => r.GetProperty("custom_id").GetString()));
        var agent = requests[0].GetProperty("params");
        Assert.Equal(new ClaudeOptions().StrongModel, agent.GetProperty("model").GetString());
        Assert.Equal(
            ReadingPrompt.System(manuscript, personas[0], GenreFrame.Unknown).Select(block => block.Text),
            agent.GetProperty("system").EnumerateArray().Select(block => block.GetProperty("text").GetString()));
        Assert.StartsWith(ReadingPrompt.Question(personas[0], StoryLength.ShortStory),
            agent.GetProperty("messages")[0].GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    /// <summary>A batched reader's reply is held to the same whole-read rule; a short one is a failure for that pass.</summary>
    [Fact]
    public async Task ReadResults_ParseEachPassAndFailAShortRead()
    {
        var (client, handler) = Create();
        handler.EnqueueOk("{\"id\":\"b\",\"processing_status\":\"ended\",\"results_url\":\"https://api.anthropic.com/v1/messages/batches/b/results\"}");
        var whole = "{\\\"verdict\\\":\\\"v\\\",\\\"assessment\\\":\\\"a\\\",\\\"strengths\\\":[\\\"s\\\"],\\\"weaknesses\\\":[\\\"w\\\"],\\\"condition\\\":\\\"c\\\",\\\"score\\\":6,\\\"scoreReason\\\":\\\"r\\\"}";
        var shortRead = "{\\\"verdict\\\":\\\"v\\\"}";
        var section = "{\\\"summary\\\":\\\"s\\\",\\\"findings\\\":[]}";
        string Line(string id, string text) =>
            "{\"custom_id\":\"" + id + "\",\"result\":{\"type\":\"succeeded\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}]}}}\n";
        handler.Enqueue(System.Net.HttpStatusCode.OK,
            Line("pass-agent", whole) + Line("pass-genre-fan", shortRead) + Line("pass-structure", section), "application/binary");

        var results = await client.ReadResults("b",
            [PersonaCatalogue.ById("agent"), PersonaCatalogue.ById("genre-fan"), PersonaCatalogue.ById("structure")],
            CancellationToken.None);

        Assert.Contains("\"score\":6", Assert.Single(results, r => r.PersonaId == "agent").Json, StringComparison.OrdinalIgnoreCase);
        var fan = Assert.Single(results, r => r.PersonaId == "genre-fan");
        Assert.Null(fan.Json);
        Assert.NotNull(fan.Failure);
        Assert.NotNull(Assert.Single(results, r => r.PersonaId == "structure").Json);
    }

    [Fact]
    public async Task AReadingPass_RequestsItsOwnEffortSetting()
    {
        var handler = new FakeHttpMessageHandler();
        var client = new ClaudeAnalysisClient(new HttpClient(handler), Options.Create(new ClaudeOptions
        {
            ApiKey = "test-key", Model = "test-model", MaxTokens = 256, PassEffort = "low"
        }));
        handler.EnqueueOk(Reply("{\"summary\":\"\",\"findings\":[]}"));

        await client.ReadSection(PersonaCatalogue.Sections[0], "the manuscript", GenreFrame.Unknown, CancellationToken.None);

        Assert.Contains("\"output_config\":{\"effort\":\"low\"}", Assert.Single(handler.RequestBodies));
    }

    [Fact]
    public void AReadingPassGetsRoomForItsReasoningAsWellAsItsReply()
    {
        // 8,000 was spent entirely on reasoning with nothing written, on every attempt at the first read.
        Assert.True(new ClaudeOptions().PassMaxTokens >= 16_000);
    }

    [Fact]
    public void TheReductionsGetRoomForTheirReply()
    {
        // 16,000 and 20,000 were both hit repeatedly on a real book, so the ceilings sit above what was
        // observed rather than at it.
        var options = new ClaudeOptions();
        Assert.True(options.AnalysisMaxTokens >= 24_000);
        Assert.True(options.CraftMaxTokens >= 32_000);
    }

    // The scene prompt needs the people and places the scene names, not the whole book's cast: sending
    // every entity cost ~95,000 input tokens per scene, uncached, 67 times over.
    [Fact]
    public async Task DescribeSceneImage_SendsOnlyTheEntitiesTheSceneNames()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("A knight at a gate."));
        var bible = new DesignBible(
            [new EntityProfile("Aldric", "a grey-eyed knight", "tall", ["the Knight"]),
             new EntityProfile("Zephyrine", "a distant queen in violet", "average", [])],
            [new EntityProfile("The Keep", "a stone tower", "huge", []),
             new EntityProfile("Saltmarsh", "a salt flat", "vast", [])]);

        await client.DescribeSceneImage(
            "The knight reached the Keep at dusk.", bible, "ink wash", [], CancellationToken.None);

        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("grey-eyed knight", body);
        Assert.Contains("stone tower", body);
        Assert.DoesNotContain("violet", body);
        Assert.DoesNotContain("salt flat", body);
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
    public async Task AssessVoiceConsistency_ParsesDominantVoiceAndDivergences()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"dominantVoice\":\"Lyrical close third-person.\",\"divergences\":[" +
            "{\"chapter\":\"Chapter 5\",\"excerpt\":\"It was bad. Real bad.\"," +
            "\"divergence\":\"Clipped noir voice unlike the rest.\",\"suggestion\":\"Restore the cadence.\"}," +
            "{\"chapter\":\"Chapter 9\",\"excerpt\":\"   \",\"divergence\":\"x\",\"suggestion\":\"y\"}]}"));

        var result = await client.AssessVoiceConsistency(
            [new ChapterSample("Chapter 1", "lyrical prose"), new ChapterSample("Chapter 5", "clipped prose")],
            CancellationToken.None);

        Assert.Equal("Lyrical close third-person.", result.DominantVoice);
        // The blank-excerpt divergence is dropped (nothing for the author to find).
        var divergence = Assert.Single(result.Divergences);
        Assert.Equal("Chapter 5", divergence.Chapter);
        Assert.Equal("It was bad. Real bad.", divergence.Excerpt);
        Assert.Equal("Restore the cadence.", divergence.Suggestion);
    }

    [Fact]
    public async Task AssessVoiceConsistency_SkipsTheCall_WithFewerThanTwoSamples()
    {
        var (client, handler) = Create(); // nothing queued: a call would throw "no response"

        var result = await client.AssessVoiceConsistency(
            [new ChapterSample("Chapter 1", "the only chapter")], CancellationToken.None);

        Assert.Equal(VoiceConsistency.Empty, result); // one chapter gives nothing to compare against
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnalyzeChapter_ParsesRunningState_AndSendsThePriorStateInThePrompt()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"keyEvents\":[\"the gate opens\"],\"runningState\":\"The hero has entered the castle.\"}"));

        var notes = await client.AnalyzeChapter(
            "excerpt", "Chapter 2", [], "Previously: the hero set out.", CancellationToken.None);

        Assert.Equal("The hero has entered the castle.", notes.RunningState);
        Assert.Contains("Previously: the hero set out.", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task AnalyzeChapter_MissingRunningState_KeepsThePriorState()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"keyEvents\":[\"the gate opens\"]}"));

        var notes = await client.AnalyzeChapter(
            "excerpt", "Chapter 2", [], "the story so far", CancellationToken.None);

        // A reply that omits runningState must not wipe the running summary mid-book.
        Assert.Equal("the story so far", notes.RunningState);
    }

    [Fact]
    public async Task AnalyzeChapter_ParsesTheExpansionRawSignals()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"summarizedMoments\":[\"the war is summarized in a paragraph\"]," +
            "\"thinSpots\":[\"the reunion runs rushed\"]," +
            "\"offPageEvents\":[\"the duel happens off-page\"]}"));

        var notes = await client.AnalyzeChapter("excerpt", "Chapter 4", [], string.Empty, CancellationToken.None);

        Assert.Equal("the war is summarized in a paragraph", Assert.Single(notes.SummarizedMoments));
        Assert.Equal("the reunion runs rushed", Assert.Single(notes.ThinSpots));
        Assert.Equal("the duel happens off-page", Assert.Single(notes.OffPageEvents));
    }

    [Fact]
    public async Task ProposeExpansion_ParsesAdditionsShowTellAndLength()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"scenesToAdd\":[{\"kind\":\"CharacterDeepening\",\"location\":\"Chapter 4\"," +
            "\"missing\":\"the reader never sees her grieve\",\"why\":\"the loss lands flat\",\"priority\":\"Must\"}," +
            "{\"kind\":\"nonsense\",\"location\":\"\",\"missing\":\"\",\"why\":\"\",\"priority\":\"urgent\"}]," +
            "\"showVsTell\":[{\"chapter\":\"Chapter 2\",\"excerpt\":\"Years passed.\",\"direction\":\"Dramatize\"," +
            "\"suggestion\":\"this leap could be a scene\"}]," +
            "\"targetLow\":90000,\"targetHigh\":100000}"));

        var result = await client.ProposeExpansion("Title", [ChapterNotes.Empty], "", wordCount: 72_000, genre: "epic fantasy", CancellationToken.None);

        var add = Assert.Single(result.ScenesToAdd);   // the empty-missing addition is dropped
        Assert.Equal(AdditionKind.CharacterDeepening, add.Kind);
        Assert.Equal("Must", add.Priority);
        Assert.Equal("the reader never sees her grieve", add.Missing);

        var note = Assert.Single(result.ShowVsTell);
        Assert.Equal(ShowTellDirection.Dramatize, note.Direction);
        Assert.Equal("Years passed.", note.Excerpt);

        Assert.Equal(72_000, result.Length.WordCount);
        Assert.Equal(90_000, result.Length.TargetLow);
        Assert.Contains("short", result.Length.Note, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProposeExpansion_BlankReply_DegradesToEmpty()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("no json here"));

        var result = await client.ProposeExpansion("Title", [ChapterNotes.Empty], "", 72_000, "epic fantasy", CancellationToken.None);

        Assert.Empty(result.ScenesToAdd);
        Assert.Empty(result.ShowVsTell);
        // Even on a blank reply, the measured length line is still produced (the range just degrades away).
        Assert.Equal(72_000, result.Length.WordCount);
    }

    [Fact]
    public async Task AssessSeriesContinuity_ParsesBreaksAndCarriedThreads()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"breaks\":[{\"chapter\":\"Chapter 1\",\"established\":\"grey eyes\",\"contradiction\":\"now green\"}," +
            "{\"chapter\":\"Chapter 2\",\"established\":\"x\",\"contradiction\":\"   \"}]," +
            "\"carriedThreads\":[{\"established\":\"the gate mechanism\",\"status\":\"contradicted\"}," +
            "{\"established\":\"the missing heir\",\"status\":\"whoknows\"}," +
            "{\"established\":\"   \",\"status\":\"Addressed\"}]}"));

        var canon = new SeriesCanon("Book One",
            [new CanonEntity("Hero", "grey eyes")], [], "the hero reached the castle",
            [new ReportFinding("Chapter 3", "the gate mechanism")]);

        var result = await client.AssessSeriesContinuity(
            "Book Two", canon, "", null, [ChapterNotes.Empty], "the hero rests", CancellationToken.None);

        Assert.Equal("Book One", result.PriorBookTitle);
        // The blank-contradiction break is dropped (nothing to report).
        var brk = Assert.Single(result.Breaks);
        Assert.Equal("Chapter 1", brk.Chapter);
        Assert.Equal("now green", brk.Contradiction);
        Assert.Equal("grey eyes", brk.Established);
        // Two carried threads survive (the blank-established one is dropped); an unknown status normalizes to Ignored.
        Assert.Equal(2, result.CarriedThreads.Count);
        Assert.Equal("Contradicted", result.CarriedThreads[0].Status);
        Assert.Equal("Ignored", result.CarriedThreads[1].Status);   // "whoknows" → the conservative still-open verdict
        // The prior book's canon (its open thread) is sent so the model can judge it.
        Assert.Contains("the gate mechanism", handler.RequestBodies[0]);
    }

    /// <summary>
    /// Against the series bible (R22–R24): the bible is sent rather than the bare canon, the earlier-written
    /// book is named canon, each finding comes back with its kind, the earlier book and its quote, and a
    /// guessed placement is sent and said back as the read's assumption.
    /// </summary>
    [Fact]
    public async Task AssessSeriesContinuity_ChecksAgainstTheBibleAndCarriesKindBookQuoteAndAssumption()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"breaks\":[{\"kind\":\"character drift\",\"chapter\":\"Chapter 3\",\"established\":\"scarred left hand\","
            + "\"contradiction\":\"her right hand is scarred\",\"book\":\"The Salt Road\",\"quote\":\"her scarred left hand\"},"
            + "{\"kind\":\"nonsense\",\"contradiction\":\"the flood comes early\"}],\"carriedThreads\":[]}"));
        var placement = Folio.Domain.Series.StoryPlacement.After("The Salt Road");

        var result = await client.AssessSeriesContinuity(
            "The Tide Ledger", new SeriesCanon("The Salt Road", [], [], "", []),
            "SERIES BIBLE: Wren Abernay, quoted \"her scarred left hand\"", placement,
            [ChapterNotes.Empty], "", CancellationToken.None);

        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("SERIES BIBLE: Wren Abernay", body);
        Assert.Contains("canon", body);
        Assert.Contains("Timeline error", body);
        Assert.Contains("not yet confirmed by the author", body);

        Assert.Equal(ContinuityKinds.CharacterDrift, result.Breaks[0].Kind);
        Assert.Equal("The Salt Road", result.Breaks[0].Book);
        Assert.Equal("her scarred left hand", result.Breaks[0].Quote);
        Assert.Equal(ContinuityKinds.ContradictedFact, result.Breaks[1].Kind);
        Assert.Equal(placement.Assumption, result.Assumption);
    }

    [Fact]
    public async Task AssessSeriesContinuity_BlankReply_DegradesToEmpty_KeepingThePriorTitle()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("no json here"));

        var canon = new SeriesCanon("Book One", [], [], "", []);
        var result = await client.AssessSeriesContinuity(
            "Book Two", canon, "", null, [ChapterNotes.Empty], "", CancellationToken.None);

        Assert.Equal("Book One", result.PriorBookTitle);
        Assert.False(result.HasContent);
    }

    [Fact]
    public async Task ReconcileFindings_ParsesMatchesAndNulls()
    {
        var (client, handler) = Create();
        var thread = Guid.NewGuid();
        handler.EnqueueOk(Reply(
            "{\"matches\":[{\"index\":0,\"threadId\":\"" + thread + "\"}," +
            "{\"index\":1,\"threadId\":null}," +
            "{\"index\":2,\"threadId\":\"not-a-guid\"}]}"));

        var prior = new[] { new PriorFinding(thread, FindingCategory.PlotHole, "Chapter 1", "The gate", 1, true) };
        var candidates = new[]
        {
            new CandidateFinding(0, FindingCategory.PlotHole, "Chapter 1", "The gate still opens"),
            new CandidateFinding(1, FindingCategory.Pacing, "Chapter 5", "New sag"),
            new CandidateFinding(2, FindingCategory.Inaccuracy, "Chapter 2", "Wrong date")
        };

        var result = await client.ReconcileFindings(prior, candidates, CancellationToken.None);

        Assert.Equal(thread, result.Matches.Single(m => m.Index == 0).ThreadId);
        Assert.Null(result.Matches.Single(m => m.Index == 1).ThreadId);
        Assert.Null(result.Matches.Single(m => m.Index == 2).ThreadId);   // unparseable id → treated as new
    }

    [Fact]
    public async Task ReconcileFindings_WithNoPriorThreads_SkipsTheCall_AndReturnsAllNew()
    {
        var (client, handler) = Create(); // nothing queued: a call would throw "no response"

        var candidates = new[] { new CandidateFinding(0, FindingCategory.PlotHole, "Chapter 1", "A hole") };
        var result = await client.ReconcileFindings([], candidates, CancellationToken.None);

        Assert.Null(Assert.Single(result.Matches).ThreadId);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ReconcileFindings_BlankReply_DegradesToEmpty()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("not json at all"));

        var prior = new[] { new PriorFinding(Guid.NewGuid(), FindingCategory.PlotHole, "Chapter 1", "The gate", 1, true) };
        var candidates = new[] { new CandidateFinding(0, FindingCategory.PlotHole, "Chapter 1", "The gate") };

        var result = await client.ReconcileFindings(prior, candidates, CancellationToken.None);

        Assert.Empty(result.Matches);   // FindingReconciler then treats every candidate as new
    }

    [Fact]
    public async Task DeriveArtStyle_ParsesTheStyle()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"artStyle\":\"gritty noir ink-wash with high-contrast lighting\"}"));

        var style = await client.DeriveArtStyle("sample prose", new DesignBible([], []), CancellationToken.None);

        Assert.Equal("gritty noir ink-wash with high-contrast lighting", style);
    }

    [Fact]
    public async Task DeriveArtStyle_ReturnsEmpty_OnAReplyWithoutJson()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("I couldn't decide on a style."));

        var style = await client.DeriveArtStyle("sample", new DesignBible([], []), CancellationToken.None);

        Assert.Equal(string.Empty, style);
    }

    [Fact]
    public async Task DescribeSceneImage_ReturnsPlainPrompt_AndPassesMomentsToAvoid()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("A knight kneels in the rain before a shattered gate."));

        var prompt = await client.DescribeSceneImage(
            "scene text", new DesignBible([], []), "noir ink wash",
            ["A knight standing at the gate"], CancellationToken.None);

        Assert.Equal("A knight kneels in the rain before a shattered gate.", prompt);
        var body = handler.RequestBodies[0];
        // The refused moment is sent so the model picks a different beat, and the image must be wordless.
        Assert.Contains("A knight standing at the gate", body);
        Assert.Contains("EMOTIONAL PEAK", body);
        Assert.Contains("NO lettering", body);
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

        var notes = await client.AnalyzeChapter("excerpt", "Chapter 3", ["Alice"], string.Empty, CancellationToken.None);

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

        var notes = await client.AnalyzeChapter("excerpt", "Chapter 1", [], string.Empty, CancellationToken.None);

        Assert.Empty(notes.CharactersPresent);
        Assert.Empty(notes.NotableStrengths);
    }

    [Fact]
    public async Task AnalyzeChapter_StampsTheChapterOnTheNotes_AndSendsItAsContext()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"keyEvents\":[\"The gate opens\"]}"));

        var notes = await client.AnalyzeChapter("excerpt", "Chapters 7–8", [], string.Empty, CancellationToken.None);

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

        var notes = await client.AnalyzeChapter("excerpt", "Chapter 4", [], string.Empty, CancellationToken.None);

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
            "\"reviews\":[{\"personaId\":\"developmental-editor\",\"verdict\":\"promising\"," +
            "\"assessment\":\"a sustained read\"," +
            "\"strengths\":[\"voice\"],\"weaknesses\":[\"pacing\"],\"condition\":\"revise middle\"}]," +
            "\"revisionPlan\":[{\"priority\":\"High\",\"issue\":\"sagging middle\",\"suggestedFix\":\"cut subplot\"}]," +
            "\"comparableTitles\":[\"Book X\"],\"marketPositioning\":\"YA fantasy\"}"));

        var report = await client.ConsolidateAnalysis(
            "Quest", [ChapterNotes.Empty], new DesignBible([], []), "", CancellationToken.None);

        Assert.Equal("A girl quests.", report.Logline);
        var character = Assert.Single(report.Characters);
        Assert.Equal("Alice", character.Name);
        Assert.Equal("protagonist", character.Role);
        Assert.Equal("developmental-editor", Assert.Single(report.Reviews).PersonaId);
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

    /// <summary>
    /// A line edit's "kind" is what lets the manuscript's margin note say something specific ("Filter
    /// word", "Cliché") instead of a blanket "Unclear". Parsed the same way every other free-text
    /// classification in this client is: snapped to the closed set, case-insensitively.
    /// </summary>
    [Fact]
    public async Task ConsolidateCraft_ParsesTheLineEditKind()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"lineEdits\":[{\"chapter\":\"Chapter 2\",\"excerpt\":\"x\",\"kind\":\"filterword\"}]}"));

        var craft = await client.ConsolidateCraft("Quest", [ChapterNotes.Empty], CancellationToken.None);

        Assert.Equal(LineEditKind.FilterWord, Assert.Single(craft.LineEdits).Kind);
    }

    /// <summary>An omitted or unrecognized kind degrades to Other rather than a guessed diagnosis.</summary>
    [Fact]
    public async Task ConsolidateCraft_UnrecognizedLineEditKind_DegradesToOther()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"lineEdits\":[{\"chapter\":\"Chapter 2\",\"excerpt\":\"x\",\"kind\":\"nonsense\"}]}"));

        var craft = await client.ConsolidateCraft("Quest", [ChapterNotes.Empty], CancellationToken.None);

        Assert.Equal(LineEditKind.Other, Assert.Single(craft.LineEdits).Kind);
    }

    /// <summary>A dialogue note's excerpt is what lets it anchor to the manuscript as a mark.</summary>
    [Fact]
    public async Task ConsolidateCraft_ParsesTheDialogueNoteExcerpt()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"dialogueNotes\":[{\"chapter\":\"Chapter 2\",\"speakers\":\"Ruth\",\"excerpt\":\"nobody spoke\"}]}"));

        var craft = await client.ConsolidateCraft("Quest", [ChapterNotes.Empty], CancellationToken.None);

        Assert.Equal("nobody spoke", Assert.Single(craft.DialogueNotes).Excerpt);
    }

    /// <summary>Scene outcome and excerpt are what let a scene beat become a Check or Dele mark.</summary>
    [Fact]
    public async Task ConsolidateCraft_ParsesTheSceneBeatExcerptAndOutcome()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"sceneBreakdown\":[{\"chapter\":\"Chapter 2\",\"scene\":\"The walk\"," +
            "\"excerpt\":\"nobody spoke\",\"outcome\":\"doesnowork\"}]}"));

        var craft = await client.ConsolidateCraft("Quest", [ChapterNotes.Empty], CancellationToken.None);

        var beat = Assert.Single(craft.SceneBreakdown);
        Assert.Equal("nobody spoke", beat.Excerpt);
        Assert.Equal(SceneVerdict.DoesNoWork, beat.Outcome);
    }

    /// <summary>An unparseable outcome defaults to EarnsItsPlace, never striking a scene unjudged.</summary>
    [Fact]
    public async Task ConsolidateCraft_UnrecognizedSceneOutcome_DegradesToEarnsItsPlace()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"sceneBreakdown\":[{\"chapter\":\"Chapter 2\",\"scene\":\"The walk\",\"outcome\":\"nonsense\"}]}"));

        var craft = await client.ConsolidateCraft("Quest", [ChapterNotes.Empty], CancellationToken.None);

        Assert.Equal(SceneVerdict.EarnsItsPlace, Assert.Single(craft.SceneBreakdown).Outcome);
    }

    [Fact]
    public async Task AnalyzeChapter_GathersTheCraftObservations_WhileTheSentencesAreStillAvailable()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(
            "{\"lineEditCandidates\":[\"\\\"He felt the cold.\\\" — cut the filter\"]," +
            "\"proseObservations\":[\"Filter words recur\"],\"dialogueObservations\":[\"Alice and Bob sound alike\"]," +
            "\"sceneBeats\":[\"Alice waits | stalls the confrontation | does no work\"]}"));

        var notes = await client.AnalyzeChapter("excerpt", "Chapter 2", [], string.Empty, CancellationToken.None);

        Assert.Single(notes.LineEditCandidates);
        Assert.Single(notes.ProseObservations);
        Assert.Single(notes.DialogueObservations);
        Assert.Single(notes.SceneBeats);
        // By consolidation time only the notes survive, so the prompt must gather these per chunk.
        Assert.Contains("lineEditCandidates", handler.RequestBodies[0]);
        Assert.Contains("sceneBeats", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task ExtractEntities_UsesExtractionMaxTokens_NotDefault()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new ClaudeOptions
        {
            ApiKey = "test-key", Model = "test-model", MaxTokens = 256, ExtractionMaxTokens = 16000
        });
        var client = new ClaudeAnalysisClient(new HttpClient(handler), options);
        handler.EnqueueOk(Reply("{\"characters\":[],\"settings\":[]}"));

        await client.ExtractEntities("excerpt", CancellationToken.None);

        // Extraction is EXHAUSTIVE over a whole (large) chunk, so its JSON output outgrows the small
        // default MaxTokens; a truncated reply is thrown away wholesale and eventually fails the chunk,
        // so this call must request its own large budget rather than the default.
        Assert.Contains("\"max_tokens\":16000", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task AnalyzeChapter_UsesChapterNotesMaxTokens_NotDefault()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new ClaudeOptions
        {
            ApiKey = "test-key", Model = "test-model", FastModel = "test-fast-model",
            MaxTokens = 256, ChapterNotesMaxTokens = 16000
        });
        var client = new ClaudeAnalysisClient(new HttpClient(handler), options);
        handler.EnqueueOk(Reply("{}"));

        await client.AnalyzeChapter("excerpt", "Chapter 1", Array.Empty<string>(), "", CancellationToken.None);

        // A chunk's notes cover story and craft for up to ~48K characters; at the small default budget
        // a full reply is cut off, thrown away wholesale, and the chunk fails after its retries.
        Assert.Contains("\"max_tokens\":16000", handler.RequestBodies[0]);
    }

    [Fact]
    public void ChapterNotesMaxTokens_DefaultHoldsAFullChunkOfNotes()
    {
        // The 12,000 default was cut off on real chunks (stop_reason=max_tokens), failing 8 of 9 chunks in
        // one book. The default must stay above that budget with room for a whole chunk's craft notes.
        Assert.True(new ClaudeOptions().ChapterNotesMaxTokens >= 16_000);
    }

    [Fact]
    public async Task AnalyzeChapter_TruncatedReply_RetriesOnceWithDoubleTheBudget()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new ClaudeOptions
        {
            ApiKey = "test-key", Model = "test-model", FastModel = "test-fast-model",
            MaxTokens = 256, ChapterNotesMaxTokens = 16000
        });
        var client = new ClaudeAnalysisClient(new HttpClient(handler), options);
        // A dense chunk (a long annals excerpt) can outgrow even the full budget; the same chunk is
        // asked again with more room rather than failing for good and stalling the whole book.
        handler.Enqueue(HttpStatusCode.OK, Sse(
            ("content_block_delta", Delta("{\"keyEvents\":[\"a")),
            ("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\"}}"),
            ("message_stop", "{\"type\":\"message_stop\"}")), "text/event-stream");
        handler.EnqueueOk(Reply("{\"keyEvents\":[\"a king dies\"]}"));

        var notes = await client.AnalyzeChapter("excerpt", "Chapter 1", Array.Empty<string>(), "", CancellationToken.None);

        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains("\"max_tokens\":16000", handler.RequestBodies[0]);
        Assert.Contains("\"max_tokens\":32000", handler.RequestBodies[1]);
        Assert.Equal("a king dies", Assert.Single(notes.KeyEvents));
    }

    [Fact]
    public async Task AnalyzeChapter_TruncatedTwice_StillThrows()
    {
        var handler = new FakeHttpMessageHandler();
        var options = Options.Create(new ClaudeOptions
        {
            ApiKey = "test-key", Model = "test-model", FastModel = "test-fast-model",
            MaxTokens = 256, ChapterNotesMaxTokens = 16000
        });
        var client = new ClaudeAnalysisClient(new HttpClient(handler), options);
        for (var i = 0; i < 2; i++)
            handler.Enqueue(HttpStatusCode.OK, Sse(
                ("content_block_delta", Delta("{\"keyEvents\":[\"a")),
                ("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\"}}"),
                ("message_stop", "{\"type\":\"message_stop\"}")), "text/event-stream");

        await Assert.ThrowsAsync<ResponseTruncatedException>(
            () => client.AnalyzeChapter("excerpt", "Chapter 1", Array.Empty<string>(), "", CancellationToken.None));
        Assert.Equal(2, handler.RequestBodies.Count);
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
            ("content_block_delta", Delta("{\"logline\":\"Ali")),
            ("content_block_delta", Delta("ce quests.\",\"synopsis\":")),
            ("content_block_delta", Delta("\"She grows up.\"}")),
            ("message_stop", "{\"type\":\"message_stop\"}")), "text/event-stream");

        var report = await client.ConsolidateAnalysis(
            "Quest", [ChapterNotes.Empty], new DesignBible([], []), "", CancellationToken.None);

        Assert.Equal("Alice quests.", report.Logline);
        Assert.Equal("She grows up.", report.Synopsis);
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


    // ---- Every call is accounted for ------------------------------------------------------------------
    //
    // A reply that fails after minutes of streaming has still been billed, and the pipeline log used to
    // say nothing about what each call sent, cost, or died of. The client is the one place that knows.

    private static (ClaudeAnalysisClient Client, FakeHttpMessageHandler Handler, CapturingLogger<ClaudeAnalysisClient> Log)
        CreateLogged()
    {
        var handler = new FakeHttpMessageHandler();
        var log = new CapturingLogger<ClaudeAnalysisClient>();
        var options = Options.Create(new ClaudeOptions
        {
            ApiKey = "test-key", Model = "test-model", FastModel = "test-fast-model", MaxTokens = 256
        });
        return (new ClaudeAnalysisClient(new HttpClient(handler), options, logger: log), handler, log);
    }

    [Fact]
    public async Task ASuccessfulCallLogsItsModelAndWhatItCost()
    {
        var (client, handler, log) = CreateLogged();
        handler.Enqueue(HttpStatusCode.OK, Sse(
            ("message_start", "{\"type\":\"message_start\",\"message\":{\"usage\":" +
                              "{\"input_tokens\":1200,\"cache_creation_input_tokens\":3000," +
                              "\"cache_read_input_tokens\":900,\"output_tokens\":1}}}"),
            ("content_block_delta", Delta("{\"artStyle\":\"ink\"}")),
            ("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}," +
                              "\"usage\":{\"output_tokens\":350}}"),
            ("message_stop", "{\"type\":\"message_stop\"}")), "text/event-stream");

        await client.DeriveArtStyle("sample", new DesignBible([], []), CancellationToken.None);

        var line = Assert.Single(log.Entries, e => e.Level == LogLevel.Information).Message;
        Assert.Contains(nameof(client.DeriveArtStyle), line);
        Assert.Contains("model=test-model", line);
        Assert.Contains("input=1200", line);
        Assert.Contains("cacheWrite=3000", line);
        Assert.Contains("cacheRead=900", line);
        Assert.Contains("output=350", line);
        Assert.Contains("stop=end_turn", line);
    }

    [Fact]
    public async Task ARejectedCallLogsTheStatusAndTheBodyAndStillThrows()
    {
        var (client, handler, log) = CreateLogged();
        handler.Enqueue(HttpStatusCode.BadRequest,
            "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"prompt is too long\"}}");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.DeriveArtStyle("sample", new DesignBible([], []), CancellationToken.None));

        var entry = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(nameof(client.DeriveArtStyle), entry.Message);
        Assert.Contains("400", entry.Message);
        Assert.Contains("prompt is too long", entry.Message);
        Assert.IsType<HttpRequestException>(entry.Exception);
    }

    [Fact]
    public async Task ATruncatedReplyIsLoggedWithWhatItCost()
    {
        var (client, handler, log) = CreateLogged();
        handler.Enqueue(HttpStatusCode.OK, Sse(
            ("message_start", "{\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":500}}}"),
            ("content_block_delta", Delta("{\"artStyle\":\"in")),
            ("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\"}," +
                              "\"usage\":{\"output_tokens\":256}}"),
            ("message_stop", "{\"type\":\"message_stop\"}")), "text/event-stream");

        await Assert.ThrowsAsync<ResponseTruncatedException>(
            () => client.DeriveArtStyle("sample", new DesignBible([], []), CancellationToken.None));

        var entry = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("input=500", entry.Message);
        Assert.Contains("output=256", entry.Message);
        Assert.Contains("stop=max_tokens", entry.Message);
    }

    [Fact]
    public async Task AStreamErrorEventIsLogged()
    {
        var (client, handler, log) = CreateLogged();
        handler.Enqueue(HttpStatusCode.OK, Sse(
            ("content_block_delta", Delta("{\"artStyle\":\"in")),
            ("error", "{\"type\":\"error\",\"error\":{\"type\":\"api_error\",\"message\":\"Internal server error\"}}")),
            "text/event-stream");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.DeriveArtStyle("sample", new DesignBible([], []), CancellationToken.None));

        var entry = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("Internal server error", entry.Message);
        Assert.Contains("streamed 15 characters", entry.Message);
    }

    // A worker killed mid-stream is the cheapest-to-miss way to pay for nothing: nothing throws a
    // message, the process just stops. The cancellation is the only trace there will ever be.
    [Fact]
    public async Task ACancelledCallIsLoggedAsWellAsRethrown()
    {
        var log = new CapturingLogger<ClaudeAnalysisClient>();
        var client = new ClaudeAnalysisClient(
            new HttpClient(new CancelledHandler()),
            Options.Create(new ClaudeOptions { ApiKey = "test-key", Model = "test-model", MaxTokens = 256 }),
            logger: log);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.DeriveArtStyle("sample", new DesignBible([], []), CancellationToken.None));

        var entry = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("cancelled", entry.Message);
        Assert.Contains(nameof(client.DeriveArtStyle), entry.Message);
    }

    [Fact]
    public async Task AReadingPassNamesThePersonaInItsLogLine()
    {
        var (client, handler, log) = CreateLogged();
        handler.Enqueue(HttpStatusCode.InternalServerError, "boom");
        var persona = PersonaCatalogue.Readers[0];

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ReadAsReader(persona, "the manuscript", GenreFrame.Unknown, CancellationToken.None));

        var entry = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(nameof(client.ReadAsReader), entry.Message);
        Assert.Contains(persona.Id, entry.Message);
    }

    // Depleted credits are not a malformed request. Classed as a plain error, a single empty balance
    // spent the attempt budget of every unit in a book within seconds and abandoned all of them for good.
    [Fact]
    public async Task AnEmptyCreditBalanceIsQuotaExhaustedNotAnOrdinaryFailure()
    {
        var (client, handler, _) = CreateLogged();
        handler.Enqueue(HttpStatusCode.BadRequest,
            "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":" +
            "\"Your credit balance is too low to access the Anthropic API. Please go to Plans & Billing to " +
            "upgrade or purchase credits.\"}}");

        var error = await Assert.ThrowsAsync<RateLimitException>(
            () => client.DeriveArtStyle("sample", new DesignBible([], []), CancellationToken.None));

        Assert.Equal(RateLimitKind.QuotaExhausted, error.Kind);
        Assert.Contains("credit balance", error.Message);
    }

    // The worker being stopped mid-call, as the HTTP stack reports it.
    private sealed class CancelledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("The worker was stopped.");
    }
    [Fact]
    public async Task TheBibleReadSharesTheReadsCachedManuscriptAndAsksForTheBooksEntries()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"entries\":[{\"kind\":\"character\",\"name\":\"Wren Abernay\","
            + "\"aliases\":[\"the pilot\"],\"era\":\"Year of the flood\",\"text\":\"A ferry pilot.\","
            + "\"quote\":\"Wren held the tiller\"},{\"kind\":\"oddity\",\"name\":\"Gullhaven\",\"text\":\"On stilts.\"},"
            + "{\"kind\":\"thread\",\"name\":\"\",\"text\":\"Nameless.\"}]}"));
        const string manuscript = "Wren held the tiller and did not look back.";
        var frame = new ReadingFrame(GenreFrame.Unknown, "SERIES BIBLE: earlier.");

        var facts = await client.ReadSeriesBible(manuscript, frame, "The Tide Ledger", "earlier entries", CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var system = body.RootElement.GetProperty("system").EnumerateArray().ToList();
        // The same cached blocks the reads send, so the bible read is a cache hit after them.
        var reads = ReadingPrompt.System(manuscript, PersonaCatalogue.All[0], frame).TakeWhile(block => block.Cached).ToList();
        for (var i = 0; i < reads.Count; i++)
        {
            Assert.Equal(reads[i].Text, system[i].GetProperty("text").GetString());
            Assert.True(system[i].TryGetProperty("cache_control", out _));
        }
        var user = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.Contains("The Tide Ledger", system[^1].GetProperty("text").GetString() + user);
        Assert.Contains("earlier entries", user);

        Assert.Equal(2, facts.Count);
        Assert.Equal(Folio.Domain.Series.BibleKind.Character, facts[0].Kind);
        Assert.Equal(["the pilot"], facts[0].Aliases);
        Assert.Equal("Year of the flood", facts[0].Era);
        Assert.Equal("Wren held the tiller", facts[0].Quote);
        // A kind it does not know is not thrown away: it is kept as an event.
        Assert.Equal(Folio.Domain.Series.BibleKind.Event, facts[1].Kind);
    }

    [Fact]
    public async Task ABibleReadWithAnUnreadableReplyFailsRatherThanEmptyingTheBooksEntries()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("Sorry, no."));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ReadSeriesBible("text", GenreFrame.Unknown, "Book", string.Empty, CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"kind\":\"between\",\"book\":\"the salt road\",\"otherBook\":\"The Tide Ledger\"}", "between \u201CThe Salt Road\u201D and \u201CThe Tide Ledger\u201D")]
    [InlineData("{\"kind\":\"before\",\"book\":\"The Salt Road\"}", "before \u201CThe Salt Road\u201D")]
    [InlineData("{\"kind\":\"era\",\"era\":\"the age before the flood\"}", "in the age before the flood")]
    // A book it was not shown, or nothing readable, is the plainest guess: after the latest-written book.
    [InlineData("{\"kind\":\"after\",\"book\":\"A Book Nobody Wrote\"}", "after \u201CThe Tide Ledger\u201D")]
    [InlineData("No idea.", "after \u201CThe Tide Ledger\u201D")]
    public async Task APlacementIsReadAgainstTheBooksItWasShown(string reply, string where)
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply(reply));

        var placement = await client.PlaceInSeries(
            "The Third Tide", ["The Salt Road", "The Tide Ledger"], "SERIES BIBLE ...", "The flood has not come yet.",
            CancellationToken.None);

        Assert.Equal(where, placement.Where);
        Assert.False(placement.Confirmed);
        var user = JsonDocument.Parse(Assert.Single(handler.RequestBodies)).RootElement
            .GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.Contains("The flood has not come yet.", user);
        Assert.Contains("SERIES BIBLE", user);
    }

    private static readonly IReadOnlyList<Review> RoundTablePanel =
    [
        new("agent", "I'd take it.", "Strong opening.", ["the hook"], ["the middle"], "a tighter middle") { Score = 8, ScoreReason = "The hook." },
        new("harsh-critic", "Not yet.", "Sags badly.", ["the prose"], ["the plot"], "a new plot") { Score = 3, ScoreReason = "The plot." },
    ];

    private static List<(string Text, bool Cached)> SystemOf(string body) =>
        [.. JsonDocument.Parse(body).RootElement.GetProperty("system").EnumerateArray()
            .Select(block => (block.GetProperty("text").GetString()!, block.TryGetProperty("cache_control", out _)))];

    private static string UserOf(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;

    /// <summary>
    /// Every round-table call opens with the same cached block, the panel's reads, so a show's thirty
    /// calls share one prefix; the speaker's own instruction follows, and the transcript is the message.
    /// </summary>
    [Fact]
    public async Task EveryRoundTableCallOpensWithTheSameCachedPanel()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"says\":\"Welcome.\",\"next\":\"agent\",\"ask\":\"What sold you?\",\"answering\":\"\",\"close\":false}"));
        handler.EnqueueOk(Reply("Hugo Hook: The opening line sold me."));
        var table = RoundTable.Empty;

        var move = await client.RoundTableHost(RoundTablePanel, table, [], 15, StoryLength.ShortStory, CancellationToken.None);
        table = RoundTablePlan.Spoken(RoundTablePlan.Apply(RoundTable.Empty, move, RoundTablePanel) with { }, "x");
        var said = await client.RoundTableSpeak(PersonaCatalogue.ById("agent"), RoundTablePanel, table,
            new RoundTableCue("agent", "What sold you?"), CancellationToken.None);

        var host = SystemOf(handler.RequestBodies[0]);
        var speaker = SystemOf(handler.RequestBodies[1]);
        Assert.Equal(host[0], speaker[0]);
        Assert.True(host[0].Cached);
        Assert.Contains("Hugo Hook", host[0].Text);
        Assert.Contains("8/10", host[0].Text);
        Assert.Contains("Tae Boll", host[1].Text);
        Assert.Contains("You are Hugo Hook", speaker[1].Text);
        Assert.Contains("score stays", speaker[1].Text);
        Assert.Contains("Tae Boll: Welcome.", UserOf(handler.RequestBodies[1]));
        Assert.Contains("What sold you?", UserOf(handler.RequestBodies[1]));

        Assert.Equal(new HostMove("Welcome.", "agent", "What sold you?", "", false), move);
        // A speaker who labels its own line has the label taken off.
        Assert.Equal("The opening line sold me.", said);
    }

    [Fact]
    public async Task TheHostIsToldTheTurnsLeftAndTheRebuttalsStillOwed()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"says\":\"Nick?\",\"next\":\"harsh-critic\",\"ask\":\"Answer Hugo.\",\"answering\":\"agent\",\"close\":false}"));

        var move = await client.RoundTableHost(RoundTablePanel, RoundTable.Empty,
            RoundTablePlan.Owed(RoundTable.Empty, RoundTablePanel), 9, StoryLength.ShortStory, CancellationToken.None);

        var user = UserOf(handler.RequestBodies[0]);
        Assert.Contains("9", user);
        Assert.Contains("Nick Picker should answer Hugo Hook", user);
        Assert.Equal("agent", move.Answering);
    }

    /// <summary>
    /// The host is told which part of the show it is in (owner's ruling): the disagreements, then the open
    /// discussion through the agenda, with what is still to raise and why; it names its topic.
    /// </summary>
    [Fact]
    public async Task TheHostIsToldThePartOfTheShowAndTheAgendaAndNamesItsTopic()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"says\":\"Structure.\",\"next\":\"agent\",\"ask\":\"Hugo?\",\"answering\":\"\",\"close\":false,\"topic\":\"Structure\"}"));
        var agreeing = new[] { RoundTablePanel[0], RoundTablePanel[1] with { Score = 7 } };
        var table = RoundTablePlan.Begin(
            [new AgendaItem("Structure", "The middle sags."), new AgendaItem("Themes", ""), new AgendaItem("Characters", "")], 15)
            with { Covered = ["Themes"] };

        var move = await client.RoundTableHost(agreeing, table, [], 15, StoryLength.ShortStory, CancellationToken.None);

        var user = UserOf(handler.RequestBodies[0]);
        Assert.Contains("OPEN DISCUSSION", user);
        Assert.Contains("Structure (still to raise): The middle sags.", user);
        Assert.Contains("Themes (raised)", user);
        Assert.Contains("disagreements", SystemOf(handler.RequestBodies[0])[1].Text);
        Assert.Equal("Structure", move.Topic);
    }

    [Fact]
    public async Task AHostMoveThatWillNotReadFailsSoTheTurnIsTriedAgain()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("Let's begin!"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.RoundTableHost(RoundTablePanel, RoundTable.Empty, [], 15, StoryLength.ShortStory, CancellationToken.None));
    }

    [Fact]
    public async Task TheCloseSaysGoodbyeAndSummarisesWithoutRuling()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"says\":\"Thanks, both.\",\"agreed\":[\"The prose sings\"],\"stillDiffer\":[\"Whether the plot holds\"]}"));

        var closing = await client.RoundTableClose(RoundTablePanel, RoundTable.Empty, CancellationToken.None);

        Assert.Equal("Thanks, both.", closing.Says);
        Assert.Equal(["The prose sings"], closing.Summary.Agreed);
        Assert.Equal(["Whether the plot holds"], closing.Summary.StillDiffer);
        Assert.Contains("never", SystemOf(handler.RequestBodies[0])[1].Text);
    }
}
