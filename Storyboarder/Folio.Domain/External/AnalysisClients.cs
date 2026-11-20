using Folio.Domain.Reading;
using Folio.Domain.Analysis;
using Storyworthtelling.Pipeline;

namespace Folio.Domain.External;

// Literary analysis via Claude: per-chunk entity extraction and the map/reduce of the revision report.
// (Bible consolidation is done deterministically by the Domain DesignBibleMerger, not the model.)
// Implementations raise RateLimitException on provider throttling.
public interface IClaudeAnalysisClient
{
    Task<DesignBibleFragment> ExtractEntities(string chunkText, CancellationToken cancellationToken);

    // Derives one cohesive illustration style for the whole book from a sample of its prose and its
    // cast, so the per-scene evidence images match the manuscript's genre and tone instead of a generic
    // default. Returns a short descriptor to prepend to every scene-image prompt (empty if it can't be
    // derived, in which case the caller keeps the default style — art direction must never stall the review).
    Task<string> DeriveArtStyle(
        string sampleText,
        DesignBible bible,
        CancellationToken cancellationToken);

    // Describes ONE wordless illustration of a scene's emotional peak — the beat the prose is measured
    // against in the fidelity check. Given the scene's text and the cast/settings, it returns a plain-text
    // visual prompt for the image model (no lettering). Best-effort: empty when it can't be derived, so a
    // scene image is never blocked on art direction. `avoidMoments` names beats already tried and refused,
    // so a retry can target a different moment of the same scene.
    /// <summary>
    /// Judges one detected scene on its own (release A3–A4): what it accomplishes, a keep / tighten /
    /// rewrite / cut call, its tension, and any inconsistencies in voice, tense, characterisation or
    /// setting. Throws when the judgement comes back without a call or without what the scene
    /// accomplishes, so the pass is retried rather than printed half-made.
    /// </summary>
    Task<Scenes.SceneJudgement> JudgeScene(Scenes.SceneToJudge scene, DesignBible bible, CancellationToken cancellationToken);

    // The batch tier (owner's ruling, R39): the same requests as the direct calls, submitted together at
    // half the cost and read back by key. A submit that cannot be made returns null, and the caller goes
    // one at a time instead.

    /// <summary>Submits one scene-judgement request per scene, keyed by scene number. Returns the batch id.</summary>
    Task<string?> SubmitSceneJudgements(
        IReadOnlyList<Scenes.SceneToJudge> scenes, DesignBible bible, CancellationToken cancellationToken);

    /// <summary>Submits one image-prompt request per scene, keyed by scene number. Returns the batch id.</summary>
    Task<string?> SubmitSceneImagePrompts(
        IReadOnlyList<Scenes.SceneImageRequest> scenes, DesignBible bible, string artStyle, CancellationToken cancellationToken);

    /// <summary>
    /// Submits whole-book passes (sections or readers) as one batch, each built exactly as its direct call,
    /// keyed by persona id. Returns the batch id.
    /// </summary>
    Task<string?> SubmitReads(
        IReadOnlyList<Reading.Persona> personas, string manuscript, Reading.ReadingFrame frame, CancellationToken cancellationToken);

    /// <summary>A finished batch's passes, parsed as the direct calls parse them, matched by persona id.</summary>
    Task<IReadOnlyList<Pipeline.PassBatchItem>> ReadResults(
        string batchId, IReadOnlyList<Reading.Persona> personas, CancellationToken cancellationToken);

    /// <summary>Whether the batch has finished processing.</summary>
    Task<bool> BatchEnded(string batchId, CancellationToken cancellationToken);

    /// <summary>A finished batch's scene judgements, matched to scenes by key; chapters names each scene's chapter.</summary>
    Task<IReadOnlyList<Pipeline.BatchItem<Scenes.SceneJudgement>>> SceneJudgementResults(
        string batchId, IReadOnlyDictionary<int, string> chapters, CancellationToken cancellationToken);

    /// <summary>A finished batch's image prompts, matched to scenes by key.</summary>
    Task<IReadOnlyList<Pipeline.BatchItem<string>>> SceneImagePromptResults(string batchId, CancellationToken cancellationToken);

    Task<string> DescribeSceneImage(
        string sceneText,
        DesignBible bible,
        string artStyle,
        IReadOnlyList<string> avoidMoments,
        CancellationToken cancellationToken);

    // Literary-analysis "map": observations for one manuscript chunk (character beats, events,
    // possible inconsistencies, checkable claims, strengths/weaknesses) anchored to the known cast.
    // chapterLabel names the chapter(s) the chunk covers ("Chapter 7", "Chapters 7–8"); it is given to
    // the model as context AND stamped onto the returned notes, so the consolidated report can cite a
    // chapter for every finding. Pass an empty label when the chapters aren't known. priorRunningState is
    // the cumulative "story so far" from the preceding chunk (empty for the first); the returned notes carry
    // it forward updated, so long-range continuity and setup/payoff survive the chunk-by-chunk map.
    Task<ChapterNotes> AnalyzeChapter(
        string chapterText,
        string chapterLabel,
        IReadOnlyList<string> characterNames,
        string priorRunningState,
        CancellationToken cancellationToken);

    // Literary-analysis "reduce", story half: consolidates all chunk notes into the story report
    // (character map + arcs, plot holes, inconsistencies/inaccuracies, multi-perspective reviews,
    // a prioritized revision plan, and publish-oriented positioning). The craft sections come back
    // empty here — see ConsolidateCraft.
    Task<BookAnalysisResult> ConsolidateAnalysis(
        string title,
        IReadOnlyList<ChapterNotes> notes,
        DesignBible bible,
        string continuitySummary,
        CancellationToken cancellationToken);

    // Literary-analysis "reduce", craft half: line-level edits, prose style & voice, dialogue
    // critique, and the scene-by-scene breakdown. A SECOND call rather than more sections on the
    // first: together they are more output than one call can produce without truncating, and a single
    // reply that long streams for long enough that the background worker is killed mid-flight. The
    // caller folds the result into the story report with BookAnalysisResult.WithCraft.
    Task<CraftReport> ConsolidateCraft(
        string title,
        IReadOnlyList<ChapterNotes> notes,
        CancellationToken cancellationToken);

    // Reads a representative prose sample from each chapter and reports where the author's own writing
    // drifts from the book's dominant authorial voice. Its own call because it needs actual prose in front
    // of the model (register, diction, cadence can't be judged from the per-chunk notes the other reduces
    // work from). Returns the dominant-voice characterization plus the divergent passages.
    Task<VoiceConsistency> AssessVoiceConsistency(
        IReadOnlyList<ChapterSample> samples,
        CancellationToken cancellationToken);

    // Cross-draft finding reconciliation: given the prior draft's finding threads and this draft's freshly
    // consolidated findings, decides which current finding is the SAME underlying issue as which prior
    // thread — even after the wording, phrasing, or chapter has moved — so a finding keeps a stable identity
    // across drafts. Returns one match per candidate (a prior ThreadId, or null when it is a new issue).
    // Best-effort: a blank/partial reply degrades to "everything is new", so reconciliation never blocks
    // the report. FindingReconciler applies these decisions to produce the persisted occurrence rows.
    Task<FindingReconciliation> ReconcileFindings(
        IReadOnlyList<PriorFinding> priorThreads,
        IReadOnlyList<CandidateFinding> candidates,
        CancellationToken cancellationToken);

    // The additive read (Phase 3): from the per-chunk notes (their summarized moments, off-page events, thin
    // spots, character beats, and setups), the running story-state, the manuscript's actual word count, and
    // the inferred genre, propose the scenes/beats to ADD (ranked by impact), the two-sided show-vs-tell
    // notes (dramatize the summarized, tighten the over-shown), and the genre-norm length range. It NUDGES,
    // never writes — pointers only, never drafted prose. Best-effort: a blank/partial reply degrades to
    // ExpansionReport.Empty so the additive read never blocks the rest of the report.
    Task<ExpansionReport> ProposeExpansion(
        string title,
        IReadOnlyList<ChapterNotes> notes,
        string continuitySummary,
        int wordCount,
        string genre,
        CancellationToken cancellationToken);

    // The cross-book continuity read (Phase 5): given the prior book's established canon (its cast/settings, its
    // final story state, and its open finding threads) and this book's per-chunk notes + story state, flag where
    // this book contradicts that canon, and report whether each of the prior book's open threads is addressed,
    // ignored, or contradicted. It NUDGES, never rewrites — pointers only. Best-effort: a blank/partial reply
    // degrades to SeriesContinuity.Empty (keeping the prior book's title) so it never blocks the report. Runs
    // only for a book linked to a prior one; a standalone book never calls it.
    /// <summary>
    /// What this book establishes for its series' bible (release G, R20): its characters, settings, world
    /// rules, timeline events and open threads, each with the story time it holds as of and a short
    /// passage quoted from the book. Sent with the reads' cached manuscript, so it is a cache hit after
    /// them. <paramref name="bibleSoFar"/> is what the earlier books established, so names match. Throws
    /// on an unreadable reply: an empty list would erase the book from the bible.
    /// </summary>
    Task<IReadOnlyList<Folio.Domain.Series.BibleFact>> ReadSeriesBible(
        string manuscript,
        Folio.Domain.Reading.ReadingFrame frame,
        string title,
        string bibleSoFar,
        CancellationToken cancellationToken);

    /// <summary>
    /// Where this book sits in its series' story time (R21), guessed from what the earlier books
    /// established and where this book's story stands. Answers only books it was shown; a reply it cannot
    /// read is the plainest guess, after the latest-written book. Never confirmed: the owner confirms.
    /// </summary>
    Task<Folio.Domain.Series.StoryPlacement> PlaceInSeries(
        string title,
        IReadOnlyList<string> otherBooks,
        string bibleText,
        string continuitySummary,
        CancellationToken cancellationToken);

    //
    // Against the series bible (release G, R22–R24) when there is one: bibleText is the earlier books' bible
    // (each entry with the book that established it and its quoted passage), the earlier-written book is
    // canon, and each finding is one of ContinuityKinds with the earlier book and its quote. placement is
    // where this book sits; while it is a guess, the result says what it assumed.
    /// <summary>
    /// The round table's host (release H, R40) makes its next move: what it says aloud, whom it hands to and
    /// what it asks, whom they are to answer, or that it would close. Told the rounds left and the rebuttals
    /// still owed (R30). Throws on a reply it cannot read, so the turn is tried again.
    /// </summary>
    Task<Folio.Domain.Reading.HostMove> RoundTableHost(
        IReadOnlyList<Review> reviews,
        Folio.Domain.Reading.RoundTable table,
        IReadOnlyList<Folio.Domain.Reading.RoundTableCue> owed,
        int roundsLeft,
        Folio.Domain.Text.StoryLength length,
        CancellationToken cancellationToken);

    /// <summary>
    /// One reader's turn at the round table (R40): in its own voice, from its own read, replying to what was
    /// said. It may concede in words; its score stays the independent one (R31). When the cue let it pass
    /// and it would only have echoed what was said, it returns an empty string.
    /// </summary>
    Task<string> RoundTableSpeak(
        Folio.Domain.Reading.Persona persona,
        IReadOnlyList<Review> reviews,
        Folio.Domain.Reading.RoundTable table,
        Folio.Domain.Reading.RoundTableCue cue,
        CancellationToken cancellationToken);

    /// <summary>The host closes the show and says where the readers ended up, without ruling (R32).</summary>
    Task<Folio.Domain.Reading.RoundTableClosing> RoundTableClose(
        IReadOnlyList<Review> reviews,
        Folio.Domain.Reading.RoundTable table,
        CancellationToken cancellationToken);

    Task<SeriesContinuity> AssessSeriesContinuity(
        string title,
        SeriesCanon priorCanon,
        string bibleText,
        Folio.Domain.Series.StoryPlacement? placement,
        IReadOnlyList<ChapterNotes> notes,
        string continuitySummary,
        CancellationToken cancellationToken);

    /// <summary>
    /// One whole-book section pass, read in character by <paramref name="persona"/>.
    ///
    /// <para>The manuscript is passed whole and <b>first</b>, because the fourteen manuscript passes share it as
    /// a cached prefix and anything persona-specific in front of it would make each pass a different
    /// prefix — measured at roughly $17 a draft against $2 (F50). <c>ReadingPrompt</c> is the one place
    /// that ordering is decided, and every implementation of this goes through it.</para>
    ///
    /// <para>A section is allowed to come back silent; a reader is not.</para>
    /// </summary>
    Task<SectionRead> ReadSection(Persona persona, string manuscript, ReadingFrame frame, CancellationToken cancellationToken);

    /// <summary>
    /// One reader's whole-book pass, in character. The reply must carry a verdict, a review, pros,
    /// cons and a condition — a short read is a failed pass, not a shorter one (F44, F46), so this
    /// throws rather than returning a thinner review and letting the page print it.
    /// </summary>
    Task<Review> ReadAsReader(Persona persona, string manuscript, ReadingFrame frame, CancellationToken cancellationToken);

    /// <summary>
    /// The two synthesis passes, which read the other fourteen rather than the manuscript: the revision
    /// plan, then the verdict. They run last for that reason. <paramref name="wordCount"/> is the
    /// manuscript's own measured length — not re-derived here, since the passes never see the
    /// manuscript itself — so the prompt can call the book what it actually is (short story, novella,
    /// or novel) rather than assuming "novel". <paramref name="findings"/> is every finding written on the
    /// manuscript, keyed, so each plan item can name the ones it draws on (R15); <paramref name="previousPlan"/>
    /// is the last draft's plan, empty on a first draft, so the plan can say what is new, still open, regressed
    /// or fixed (R19). The verdict is given neither.
    /// </summary>
    Task<SynthesisRead> Synthesise(
        string passId,
        IReadOnlyList<SectionRead> sections,
        IReadOnlyList<Review> readers,
        int wordCount,
        IReadOnlyList<Markup.FindingEntry> findings,
        IReadOnlyList<RevisionItem> previousPlan,
        CancellationToken cancellationToken);
}

// Image generation via Gemini. Reference sheets are generated once per recurring design-bible entity,
// then supplied back as reference images on each scene render to keep characters/settings consistent —
// coherence is part of the fidelity check.
public interface IGeminiImageClient
{
    Task<byte[]> GenerateReferenceSheet(string description, string artStyle, CancellationToken cancellationToken);

    // Renders one wordless evidence image for a scene's emotional peak. aspectRatio (width/height of the
    // image's slot in the report) is snapped to the nearest ratio the image model supports and requested,
    // so the ~1MP output fills the slot instead of being letterboxed. Null requests the model's default
    // (portrait) framing. Reference images keep recurring characters/settings on-model.
    /// <summary>
    /// Paints a book's jacket: the cover art the shelf and the desk show, and the report's own
    /// front. Wordless, like the scene images — the title is set in type over it by the app, because
    /// image models letter badly and a jacket with a misspelt title is worse than a plain one.
    /// </summary>
    Task<byte[]> GenerateJacket(
        string title,
        string genre,
        string logline,
        string artStyle,
        CancellationToken cancellationToken);

    Task<byte[]> GenerateSceneImage(
        string visualPrompt,
        string artStyle,
        IReadOnlyList<byte[]> referenceImages,
        double? aspectRatio,
        CancellationToken cancellationToken);

    /// <summary>
    /// Submits scenes' pictures to Gemini's batch tier, inline (owner's request: half the price, back
    /// within a day), each keyed by its scene. Answers the batch's name. Throws on a refused request.
    /// </summary>
    Task<string> SubmitSceneImages(
        IReadOnlyList<Folio.Domain.Pipeline.SceneImageJob> jobs, string artStyle, CancellationToken cancellationToken);

    /// <summary>
    /// Asks whether a batch has ended and, when it has, hands back each scene's picture or why there is
    /// none. A batch that failed, was cancelled or expired has ended with no pictures.
    /// </summary>
    Task<GeminiBatchPoll> PollSceneImages(string batchName, CancellationToken cancellationToken);
}

/// <summary>Where a Gemini batch of scene pictures stands: ended or not, and each scene's picture or failure.</summary>
public sealed record GeminiBatchPoll(bool Ended, IReadOnlyList<Folio.Domain.Pipeline.BatchItem<byte[]>> Images);

// Fetches the plain text of a source document (Google Doc or a supported uploaded format) selected via
// the in-app Drive picker, using the browser-granted OAuth access token.
public interface IGoogleDocsClient
{
    Task<string> FetchText(string documentId, string accessToken, CancellationToken cancellationToken);
}
