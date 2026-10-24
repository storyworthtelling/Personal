using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Folio.Domain.Analysis;
using Folio.Domain.Model;
using Folio.Domain.Reading;
using Folio.Domain.Pipeline;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.External;
using Folio.Tests.Support;
using Xunit;

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
    public async Task EveryCall_SetsItsEffortExplicitly_MediumForWorkAndHighForTheReads()
    {
        // Effort is set on every request rather than inherited from a default that changes with the model.
        // The extraction, notes, reductions and image prompts run at medium: at high the model's reasoning
        // shares the output limit with the reply, and the long structured replies were being cut off
        // (the reasoning took most of the 16,000-20,000 tokens, leaving too little for the JSON). The
        // sixteen whole-book reads are the product, so they keep high.
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("{\"characters\":[],\"settings\":[]}"));
        handler.EnqueueOk(Reply("{}"));
        handler.EnqueueOk(Reply("{}"));
        handler.EnqueueOk(Reply("{\"summary\":\"\",\"findings\":[]}"));

        await client.ExtractEntities("excerpt", CancellationToken.None);
        await client.AnalyzeChapter("excerpt", "Chapter 1", [], string.Empty, CancellationToken.None);
        await client.ConsolidateAnalysis("Title", [ChapterNotes.Empty], new DesignBible([], []), "", CancellationToken.None);
        await client.ReadSection(PersonaCatalogue.Sections[0], "the manuscript", CancellationToken.None);

        Assert.Equal(4, handler.RequestBodies.Count);
        foreach (var body in handler.RequestBodies.Take(3))
            Assert.Contains("\"output_config\":{\"effort\":\"medium\"}", body);
        Assert.Contains("\"output_config\":{\"effort\":\"high\"}", handler.RequestBodies[3]);
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
            "Book Two", canon, [ChapterNotes.Empty], "the hero rests", CancellationToken.None);

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

    [Fact]
    public async Task AssessSeriesContinuity_BlankReply_DegradesToEmpty_KeepingThePriorTitle()
    {
        var (client, handler) = Create();
        handler.EnqueueOk(Reply("no json here"));

        var canon = new SeriesCanon("Book One", [], [], "", []);
        var result = await client.AssessSeriesContinuity(
            "Book Two", canon, [ChapterNotes.Empty], "", CancellationToken.None);

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
            () => client.ReadAsReader(persona, "the manuscript", CancellationToken.None));

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
}
